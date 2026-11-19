using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GamepadEnhancer
{
    internal enum VhfSignatureKind
    {
        /// <summary>No signature, or the file is not a signable type.</summary>
        Unsigned,
        /// <summary>A signature is present but the file no longer matches it.</summary>
        Tampered,
        /// <summary>The signer is not trusted by Windows (a test or unknown certificate).</summary>
        UntrustedChain,
        /// <summary>The signer certificate signed itself, even if somebody made this PC trust it.</summary>
        SelfSigned,
        /// <summary>A certificate whose name says it is for testing, even if this PC trusts it.</summary>
        TestCertificate,
        Expired,
        /// <summary>Windows trusts the signature and the signer looks like a real publisher.</summary>
        Trusted
    }

    internal sealed record VhfSignature(VhfSignatureKind Kind, string Subject, string Issuer, string Thumbprint, string Detail)
    {
        public bool IsProduction => Kind == VhfSignatureKind.Trusted;

        public string Describe() => Kind switch
        {
            VhfSignatureKind.Trusted => "Signed and trusted by Windows: " + Subject,
            VhfSignatureKind.Unsigned => "Not signed",
            VhfSignatureKind.Tampered => "The file does not match its signature (changed or corrupted)",
            VhfSignatureKind.SelfSigned => "Self-signed (" + Subject + "), not a production signature",
            VhfSignatureKind.TestCertificate => "Signed with a test certificate (" + Subject + "), not a production signature",
            VhfSignatureKind.Expired => "The signing certificate has expired (" + Subject + ")",
            _ => "Signed by " + (string.IsNullOrEmpty(Subject) ? "an unknown publisher" : Subject) + ", which Windows does not trust"
        };
    }

    /// <summary>Reads and judges the Authenticode signature of one file. Injected so the package policy is testable without real certificates.</summary>
    internal interface IVhfSignatureReader
    {
        VhfSignature Read(string path);
    }

    /// <summary>
    /// The real reader: asks Windows (WinVerifyTrust, the same check <c>Get-AuthenticodeSignature</c> uses) whether the signature is valid
    /// and trusted, without network access, then refuses anything that is self-signed or says it is a test certificate even when the
    /// machine has been made to trust it (the upstream project's local test certificate is exactly that).
    /// </summary>
    internal sealed class WinTrustSignatureReader : IVhfSignatureReader
    {
        private const int TrustENoSignature = unchecked((int)0x800B0100);
        private const int TrustESubjectFormUnknown = unchecked((int)0x800B0003);
        private const int TrustEProviderUnknown = unchecked((int)0x800B0001);
        private const int TrustEBadDigest = unchecked((int)0x80096010);
        private const int CertEExpired = unchecked((int)0x800B0101);

        public VhfSignature Read(string path)
        {
            if (!File.Exists(path)) return new VhfSignature(VhfSignatureKind.Unsigned, "", "", "", "The file does not exist.");
            int result = Verify(path);
            X509Certificate2 cert = SignerOf(path);
            return Classify(result, cert?.Subject ?? "", cert?.Issuer ?? "", cert?.Thumbprint ?? "");
        }

        /// <summary>The judgement, separated from the Win32 calls so it can be tested with plain values.</summary>
        internal static VhfSignature Classify(int trustResult, string subject, string issuer, string thumbprint)
        {
            string detail = "WinVerifyTrust 0x" + trustResult.ToString("X8");
            if (trustResult == TrustENoSignature || trustResult == TrustESubjectFormUnknown || trustResult == TrustEProviderUnknown)
                return new VhfSignature(VhfSignatureKind.Unsigned, subject, issuer, thumbprint, detail);
            if (trustResult == TrustEBadDigest)
                return new VhfSignature(VhfSignatureKind.Tampered, subject, issuer, thumbprint, detail);
            if (subject.Length > 0 && string.Equals(subject, issuer, StringComparison.OrdinalIgnoreCase))
                return new VhfSignature(VhfSignatureKind.SelfSigned, subject, issuer, thumbprint, detail);
            if (trustResult == CertEExpired)
                return new VhfSignature(VhfSignatureKind.Expired, subject, issuer, thumbprint, detail);
            if (trustResult != 0)
                return new VhfSignature(VhfSignatureKind.UntrustedChain, subject, issuer, thumbprint, detail);
            if (LooksLikeTestCertificate(subject) || LooksLikeTestCertificate(issuer))
                return new VhfSignature(VhfSignatureKind.TestCertificate, subject, issuer, thumbprint, detail);
            return new VhfSignature(VhfSignatureKind.Trusted, subject, issuer, thumbprint, detail);
        }

        private static readonly Regex TestName = new(@"(^|[^a-z])test|testcert", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        internal static bool LooksLikeTestCertificate(string name) => !string.IsNullOrEmpty(name) && TestName.IsMatch(name);

        private static X509Certificate2 SignerOf(string path)
        {
            try
            {
                if (path.EndsWith(".cat", StringComparison.OrdinalIgnoreCase))
                {
                    var cms = new SignedCms();
                    cms.Decode(File.ReadAllBytes(path));
                    return cms.SignerInfos.Count > 0 ? cms.SignerInfos[0].Certificate : null;
                }
                return new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            }
            catch (CryptographicException) { return null; }   // not signed: the trust result already says so
        }

        private static int Verify(string path)
        {
            IntPtr filePath = Marshal.StringToHGlobalUni(path);
            IntPtr fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            try
            {
                var fileInfo = new WinTrustFileInfo { cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(), pcwszFilePath = filePath };
                Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);
                var data = new WinTrustData
                {
                    cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                    dwUIChoice = 2,                 // WTD_UI_NONE
                    fdwRevocationChecks = 0,        // WTD_REVOKE_NONE
                    dwUnionChoice = 1,              // WTD_CHOICE_FILE
                    pFile = fileInfoPtr,
                    dwStateAction = 1,              // WTD_STATEACTION_VERIFY
                    dwProvFlags = 0x10 | 0x1000     // no revocation check, no network (WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL)
                };
                Guid action = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");   // WINTRUST_ACTION_GENERIC_VERIFY_V2
                int result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
                data.dwStateAction = 2;             // WTD_STATEACTION_CLOSE
                WinVerifyTrust(IntPtr.Zero, ref action, ref data);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(fileInfoPtr);
                Marshal.FreeHGlobal(filePath);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustFileInfo
        {
            public uint cbStruct;
            public IntPtr pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustData
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WinTrustData pWVTData);
    }

    /// <summary>The outcome of checking a driver package folder before it is installed.</summary>
    internal sealed class VhfPackageReport
    {
        public readonly List<string> Problems = new();
        public bool Ok => Problems.Count == 0;
        public string Folder { get; set; }
        public string InfPath { get; set; }
        public string SetupToolPath { get; set; }
        public VhfSignature CatalogSignature { get; set; }
        public VhfSignature SetupToolSignature { get; set; }
        public string DriverVer { get; set; }
        public string SourceRevision { get; set; }
        public int ProtocolVersion { get; set; }
        public string Platform { get; set; }

        public string Summary()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Ok ? "The package passed the checks." : "The package was refused:");
            foreach (string p in Problems) sb.AppendLine(" - " + p);
            if (CatalogSignature != null) sb.AppendLine("Driver catalog: " + CatalogSignature.Describe());
            if (SetupToolSignature != null) sb.AppendLine("Setup tool: " + SetupToolSignature.Describe());
            if (!string.IsNullOrEmpty(DriverVer)) sb.AppendLine("Driver version: " + DriverVer + (string.IsNullOrEmpty(SourceRevision) ? "" : ", upstream revision " + SourceRevision));
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Checks a libvirtualgamepad driver package before the app runs its setup tool: exactly the expected files, every file matching the
    /// package manifest, a manifest for this protocol and this processor, and a catalog and setup tool that carry a real, trusted
    /// signature (never unsigned, self-signed or test-signed). This is a pre-check for the user's benefit; Windows' own driver
    /// installation checks (catalog membership, publisher trust, Secure Boot) remain the enforcement and are never bypassed or changed.
    /// </summary>
    internal sealed class VhfPackageVerifier
    {
        public const string InfRelative = "driver/VibeshineVhfGamepad.inf";
        public const string DllRelative = "driver/VibeshineVhfGamepad.dll";
        public const string CatRelative = "driver/VibeshineVhfGamepad.cat";
        public const string ToolRelative = "tools/VibeshineVhfGamepadDeviceSetup.exe";
        public const string ManifestRelative = "manifest.json";
        private const int MaxManifestBytes = 1 << 20;

        private static readonly string[] Required = { ManifestRelative, InfRelative, DllRelative, CatRelative, ToolRelative };

        private readonly IVhfSignatureReader signatures;
        private readonly string platform;

        public VhfPackageVerifier(IVhfSignatureReader signatures, string platform = null)
        {
            this.signatures = signatures ?? throw new ArgumentNullException(nameof(signatures));
            this.platform = platform ?? CurrentPlatform();
        }

        public static string CurrentPlatform() => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "ARM64" : "x64";

        public VhfPackageReport Verify(string folder)
        {
            var report = new VhfPackageReport { Folder = folder };
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) { report.Problems.Add("The package folder does not exist."); return report; }
            string root = Path.GetFullPath(folder);

            // 1. exactly the expected files
            var present = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                .ToList();
            foreach (string need in Required)
                if (!present.Contains(need, StringComparer.OrdinalIgnoreCase)) report.Problems.Add("Missing file: " + need);
            foreach (string extra in present.Where(p => !Required.Contains(p, StringComparer.OrdinalIgnoreCase)))
                report.Problems.Add("Unexpected file in the package: " + extra);
            if (!report.Ok) return report;

            report.InfPath = Path.Combine(root, "driver", "VibeshineVhfGamepad.inf");
            report.SetupToolPath = Path.Combine(root, "tools", "VibeshineVhfGamepadDeviceSetup.exe");

            // 2. the manifest, and every payload file against it
            JsonDocument manifest = ReadManifest(Path.Combine(root, "manifest.json"), report);
            if (manifest != null)
            {
                using (manifest) CheckManifest(manifest.RootElement, root, report);
            }

            // 3. signatures: the catalog (it vouches for the driver files) and the setup tool that is about to be run elevated
            report.CatalogSignature = signatures.Read(Path.Combine(root, "driver", "VibeshineVhfGamepad.cat"));
            report.SetupToolSignature = signatures.Read(report.SetupToolPath);
            if (!report.CatalogSignature.IsProduction) report.Problems.Add("The driver catalog: " + report.CatalogSignature.Describe() + ".");
            if (!report.SetupToolSignature.IsProduction) report.Problems.Add("The setup tool: " + report.SetupToolSignature.Describe() + ".");
            return report;
        }

        private static JsonDocument ReadManifest(string path, VhfPackageReport report)
        {
            try
            {
                if (new FileInfo(path).Length > MaxManifestBytes) { report.Problems.Add("manifest.json is unreasonably large."); return null; }
                return JsonDocument.Parse(File.ReadAllBytes(path));
            }
            catch (JsonException ex) { report.Problems.Add("manifest.json is not valid JSON: " + ex.Message); return null; }
        }

        private void CheckManifest(JsonElement m, string root, VhfPackageReport report)
        {
            if (m.ValueKind != JsonValueKind.Object) { report.Problems.Add("manifest.json is not a JSON object."); return; }

            if (!TryInt(m, "schema_version", out int schema) || schema != 1) report.Problems.Add("manifest.json has an unsupported schema_version.");
            if (!TryInt(m, "protocol_version", out int protocol)) report.Problems.Add("manifest.json has no protocol_version.");
            else
            {
                report.ProtocolVersion = protocol;
                if (protocol != VhfProtocol.Version) report.Problems.Add($"The package speaks protocol {protocol}; this app speaks {VhfProtocol.Version}.");
            }
            report.Platform = TryString(m, "platform");
            if (!string.Equals(report.Platform, platform, StringComparison.OrdinalIgnoreCase))
                report.Problems.Add($"The package is for {report.Platform ?? "an unknown platform"}; this PC needs {platform}.");
            report.DriverVer = TryString(m, "driver_ver");
            if (string.IsNullOrWhiteSpace(report.DriverVer)) report.Problems.Add("manifest.json has no driver_ver.");
            report.SourceRevision = TryString(m, "source_revision");
            if (report.SourceRevision == null || !Regex.IsMatch(report.SourceRevision, "^[0-9a-fA-F]{40,64}$")) report.Problems.Add("manifest.json has no valid source_revision.");

            if (!m.TryGetProperty("files", out JsonElement files) || files.ValueKind != JsonValueKind.Array) { report.Problems.Add("manifest.json lists no files."); return; }
            var listed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement f in files.EnumerateArray())
            {
                string p = TryString(f, "path"), h = TryString(f, "sha256");
                if (p != null && h != null) listed[p.Replace('\\', '/')] = h;
            }
            foreach (string rel in new[] { InfRelative, DllRelative, CatRelative, ToolRelative })
            {
                if (!listed.TryGetValue(rel, out string expected)) { report.Problems.Add(rel + " is not listed in manifest.json."); continue; }
                string actual = Sha256Of(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    report.Problems.Add(rel + " does not match its SHA-256 in manifest.json (changed or corrupted).");
            }
        }

        private static bool TryInt(JsonElement e, string name, out int value)
        {
            value = 0;
            return e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out value);
        }

        private static string TryString(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        public static string Sha256Of(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        /// <summary>
        /// Unpacks a package .zip into a new empty folder, refusing anything that could write outside it (rooted or ".." paths), more than
        /// a few entries, or an unreasonable size. The unpacked folder is then verified like any other.
        /// </summary>
        public static string ExtractZip(string zipPath, string destination)
        {
            const int maxEntries = 16;
            const long maxTotal = 64L * 1024 * 1024;
            Directory.CreateDirectory(destination);
            if (Directory.EnumerateFileSystemEntries(destination).Any()) throw new InvalidOperationException("The destination folder is not empty.");
            string destFull = Path.GetFullPath(destination);
            string prefix = destFull.EndsWith(Path.DirectorySeparatorChar) ? destFull : destFull + Path.DirectorySeparatorChar;

            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            if (zip.Entries.Count > maxEntries) throw new InvalidDataException("The package has too many files.");
            if (zip.Entries.Sum(e => e.Length) > maxTotal) throw new InvalidDataException("The package is too large.");
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;   // a folder entry
                string target = Path.GetFullPath(Path.Combine(destFull, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The package contains a path outside its folder: " + entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                entry.ExtractToFile(target, overwrite: false);
            }
            return destFull;
        }
    }
}
