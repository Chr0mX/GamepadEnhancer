/* GamepadEnhancer - site script. Plain JS, no dependencies.
   The two labs re-implement the app's real maths (Core/StickMath.cs, ControllerEngine.cs) so the pages can show it live. */
(function () {
  'use strict';

  /* ---------- shared maths (ported from the C# engine) ---------- */
  var clamp = function (v, a, b) { return Math.min(b, Math.max(a, v)); };
  // C# Math.Round rounds halves to even; JS Math.round rounds them up. Match C#.
  function roundEven(v) {
    var f = Math.floor(v), d = v - f;
    if (d < 0.5) return f;
    if (d > 0.5) return f + 1;
    return f % 2 === 0 ? f : f + 1;
  }
  var ds4Axis = function (p) { return clamp(roundEven(128 + clamp(p, -100, 100) * 127 / 100), 0, 255); };
  var xboxAxis = function (p) { return roundEven(clamp(p, -100, 100) * 32767 / 100); };
  var TANH1 = Math.tanh(1);

  function driftVector(pull, d) {           // one straight axis, plain sign of the percent (Core/ControllerEngine.GetBasePull)
    switch (pull) {
      case 'Up': return [0, -d];
      case 'Left': return [-d, 0];
      case 'Right': return [d, 0];
      default: return [0, d];                // Down
    }
  }
  var wiggleOnX = function (pull) { return pull === 'Up' || pull === 'Down'; };   // the wiggle goes across the drift
  var smoothingTauMs = function (n) { return n > 0 ? n * 4 : 0; };
  var softWigglePeriodMs = function (n) { return n > 0 ? n * 16 : 0; };
  var lowPassAlpha = function (tau, dt) { return tau <= 0 ? 1 : (dt <= 0 ? 0 : 1 - Math.exp(-dt / tau)); };
  function softWiggleDrivePercent(n, target) {
    if (n <= 0) return 0;
    var a = n * (100 / 127) / TANH1;
    return Math.max(0, Math.min(a, 100 - Math.abs(target)));
  }
  var squareWave = function (t, period) { return period <= 0 ? 0 : (Math.floor(t / (period / 2)) % 2 === 0 ? 1 : -1); };

  function fastPeriodMs(algo, n, rateMs) {   // Core/ControllerEngine.FastPeriodMs
    var p = clamp(rateMs, 1, 50);
    if (algo === 'lp') { var w = softWigglePeriodMs(n); if (w > 0) p = Math.min(p, Math.max(1, w / 8)); }
    return p;
  }

  /* One simulated output stream: tick(tMs) -> {x, y} raw axis values (DS4 0..255 or Xbox int16). */
  function makeSim(cfg) {
    var lp = { seeded: false, x: 0, y: 0 }, high = false, lastT = 0;
    function approach(cur, target, alpha) {
      var next = cur + alpha * (target - cur);
      return Math.abs(target - next) < 0.001 ? target : next;
    }
    var conv = cfg.controller === 'xbox' ? xboxAxis : ds4Axis;
    return {
      cfg: cfg,
      tick: function (t) {
        var v = driftVector(cfg.pull, cfg.drift), tx = v[0], ty = v[1], onX = wiggleOnX(cfg.pull), n = cfg.n;
        var ox = tx, oy = ty;
        if (cfg.algo === 'lp' && n > 0) {
          var tau = smoothingTauMs(n), dt = lastT > 0 ? clamp(t - lastT, 0, 100) : 0;
          var wig = 0;
          if (lp.seeded) wig = softWiggleDrivePercent(n, onX ? tx : ty) * squareWave(t, softWigglePeriodMs(n));
          var ix = tx + (onX ? wig : 0), iy = ty + (onX ? 0 : wig), a = lowPassAlpha(tau, dt);
          if (!lp.seeded) { lp.x = ix; lp.y = iy; lp.seeded = true; }
          else { lp.x = approach(lp.x, ix, a); lp.y = approach(lp.y, iy, a); }
          ox = lp.x; oy = lp.y;
        } else { lp.seeded = false; }
        lastT = t;
        var rx = conv(ox), ry = conv(oy);
        if (cfg.algo === 'rc' && n > 0) {
          high = !high;
          var base = onX ? rx : ry, a2;
          if (cfg.controller === 'xbox') {
            var raw = roundEven(n * 32767 / 127);
            a2 = Math.min(raw, Math.min(32767 - base, base + 32768));
          } else {
            a2 = Math.min(n, Math.min(base, 255 - base));
          }
          base += high ? a2 : -a2;
          if (onX) rx = base; else ry = base;
        }
        return { x: rx, y: ry };
      }
    };
  }

  /* ---------- recoil pattern text (Core/RecoilPattern.cs) ---------- */
  function parsePattern(text) {
    var steps = [];
    String(text || '').split('|').forEach(function (seg) {
      seg = seg.trim(); if (!seg) return;
      var p = seg.split(':').map(function (s) { return s.trim(); });
      if (p.length < 2) return;
      if (!/^[+-]?\d+$/.test(p[0]) || !/^[+-]?\d+$/.test(p[1])) return;
      var delay = 80;
      if (p.length >= 3) { var d = parseFloat(p[2]); delay = isNaN(d) ? 0 : d; }
      steps.push({ x: clamp(parseInt(p[0], 10), -100, 100), y: clamp(parseInt(p[1], 10), -100, 100), ms: Math.max(0.1, delay) });
    });
    return steps;
  }
  function circlePreset() {
    var out = [];
    for (var i = 0; i < 16; i++) { var a = Math.PI * 2 * i / 16; out.push(roundEven(Math.cos(a) * 8) + ':' + roundEven(Math.sin(a) * 8) + ':17.3'); }
    return out.join('|');
  }
  function applyLift(step, radius) {          // StickMath.ApplyCenterRadius
    if (radius <= 0 || (step.x === 0 && step.y === 0)) return { x: step.x, y: step.y };
    var mag = Math.sqrt(step.x * step.x + step.y * step.y);
    if (mag >= radius) return { x: step.x, y: step.y };
    var k = radius / mag;
    return { x: clamp(roundEven(step.x * k), -100, 100), y: clamp(roundEven(step.y * k), -100, 100) };
  }
  var effectiveStepMs = function (ms) { return Math.max(10, Math.ceil(roundEven(ms) / 10) * 10); };   // steps advance on the 10 ms tick

  window.VigemSite = { roundEven: roundEven, ds4Axis: ds4Axis, xboxAxis: xboxAxis, driftVector: driftVector, makeSim: makeSim,
    parsePattern: parsePattern, circlePreset: circlePreset, applyLift: applyLift, effectiveStepMs: effectiveStepMs,
    fastPeriodMs: fastPeriodMs, softWigglePeriodMs: softWigglePeriodMs, softWiggleDrivePercent: softWiggleDrivePercent, lowPassAlpha: lowPassAlpha };

  /* ---------- page chrome ---------- */
  function ready(fn) { if (document.readyState !== 'loading') fn(); else document.addEventListener('DOMContentLoaded', fn); }
  var themeVar = function (name) { return getComputedStyle(document.documentElement).getPropertyValue(name).trim(); };

  ready(function () {
    var root = document.documentElement;

    var toggle = document.getElementById('theme-toggle');
    if (toggle) toggle.addEventListener('click', function () {
      var cur = root.getAttribute('data-theme') || (window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark');
      var next = cur === 'dark' ? 'light' : 'dark';
      root.setAttribute('data-theme', next);
      try { localStorage.setItem('vigem-theme', next); } catch (e) { /* storage may be blocked */ }
      document.dispatchEvent(new Event('themechange'));
    });

    var navBtn = document.getElementById('nav-toggle'), navList = document.getElementById('nav-links');
    if (navBtn && navList) navBtn.addEventListener('click', function () {
      var open = navList.classList.toggle('open'); navBtn.setAttribute('aria-expanded', open ? 'true' : 'false');
    });

    document.querySelectorAll('pre').forEach(function (pre) {
      if (pre.dataset.nocopy !== undefined) return;
      var b = document.createElement('button');
      b.type = 'button'; b.className = 'btn-small'; b.textContent = 'Copy'; b.setAttribute('aria-label', 'Copy code');
      b.style.cssText = 'position:absolute;top:var(--space-2);right:var(--space-2)';
      b.addEventListener('click', function () {
        var txt = pre.innerText.replace(/\nCopy$/, '');
        var done = function () { b.textContent = 'Copied'; setTimeout(function () { b.textContent = 'Copy'; }, 1400); };
        if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(txt).then(done, function () {});
      });
      pre.appendChild(b);
    });

    var year = document.getElementById('year'); if (year) year.textContent = new Date().getFullYear();

    initStickLab(); initPatternLab();
  });

  function fitCanvas(cv, cssW, cssH) {
    var dpr = Math.min(window.devicePixelRatio || 1, 2);
    var w = cv.clientWidth || cssW, h = cssH || Math.round(w * 0.6);
    cv.style.height = h + 'px';
    cv.width = Math.round(w * dpr); cv.height = Math.round(h * dpr);
    var ctx = cv.getContext('2d'); ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    return { ctx: ctx, w: w, h: h };
  }

  /* ---------- Stick lab ---------- */
  function initStickLab() {
    var lab = document.getElementById('stick-lab'); if (!lab) return;
    var $ = function (id) { return lab.querySelector('#' + id); };
    var stickCv = $('lab-stick'), chartCv = $('lab-chart');
    var st = { pull: 'Down', controller: 'ds4', algo: 'rc', drift: 25, n: 12, rate: 2, paused: false };
    var sim, samples = [], simT = 0, nextTick = 0, lastReal = 0, period = 2;
    var reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (reduce) st.paused = true;

    function rebuild() {
      sim = makeSim({ pull: st.pull, controller: st.controller, algo: st.algo, drift: st.drift, n: st.n });
      period = st.algo === 'off' ? 10 : fastPeriodMs(st.algo, st.n, st.rate);
      samples = []; simT = 0; nextTick = 0;
      var v = $('lab-rate-note'); if (v) v.textContent = st.algo === 'off' ? 'Filtering off: normal 10 ms loop (about 4 reports per second when nothing changes).' :
        'Loop period ' + period.toFixed(period % 1 ? 1 : 0) + ' ms (' + Math.round(1000 / period) + ' ticks per second).';
      if (st.paused) { for (var i = 0; i < 400; i++) { samples.push({ t: simT, x: 0, y: 0 }); } step(1500); }
      draw();
    }
    function step(dt) {
      simT += dt;
      while (nextTick <= simT) { var s = sim.tick(nextTick); samples.push({ t: nextTick, x: s.x, y: s.y }); nextTick += period; }
      while (samples.length && samples[0].t < simT - 1600) samples.shift();
    }
    var pctOf = function (raw) { return st.controller === 'xbox' ? raw * 100 / 32767 : (raw - 128) * 100 / 127; };
    var rawCenter = function () { return st.controller === 'xbox' ? 0 : 128; };

    function draw() {
      var c = themeVar;
      var a = fitCanvas(stickCv, 240, 240), ctx = a.ctx, w = a.w, h = a.h, R = Math.min(w, h) / 2 - 14, cx = w / 2, cy = h / 2;
      ctx.clearRect(0, 0, w, h);
      ctx.strokeStyle = c('--lab-axis'); ctx.lineWidth = 1; ctx.beginPath(); ctx.arc(cx, cy, R, 0, 7); ctx.stroke();
      ctx.strokeStyle = c('--lab-grid'); ctx.beginPath(); ctx.moveTo(cx - R, cy); ctx.lineTo(cx + R, cy); ctx.moveTo(cx, cy - R); ctx.lineTo(cx, cy + R); ctx.stroke();
      var upSign = st.controller === 'xbox' ? -1 : 1;          // PS4: +Y is down on screen; Xbox 360: +Y is up
      var toXY = function (rx, ry) { return [cx + pctOf(rx) / 100 * R, cy + upSign * pctOf(ry) / 100 * R]; };
      var v = driftVector(st.pull, st.drift), tgt = [cx + v[0] / 100 * R, cy + upSign * v[1] / 100 * R];
      ctx.strokeStyle = c('--lab-target'); ctx.setLineDash([4, 3]); ctx.beginPath(); ctx.arc(tgt[0], tgt[1], 6, 0, 7); ctx.stroke(); ctx.setLineDash([]);
      var last = samples.length ? samples[samples.length - 1] : null;
      ctx.fillStyle = c('--lab-trace');
      for (var i = Math.max(0, samples.length - 90); i < samples.length; i++) {
        var p = toXY(samples[i].x, samples[i].y); ctx.globalAlpha = 0.08 + 0.5 * (i - (samples.length - 90)) / 90; ctx.beginPath(); ctx.arc(p[0], p[1], 2.2, 0, 7); ctx.fill();
      }
      ctx.globalAlpha = 1;
      if (last) { var q = toXY(last.x, last.y); ctx.fillStyle = c('--lab-dot'); ctx.beginPath(); ctx.arc(q[0], q[1], 5, 0, 7); ctx.fill(); }
      ctx.fillStyle = c('--lab-text'); ctx.font = '11px ' + c('--font-family-sans'); ctx.textAlign = 'center';
      ctx.fillText('up', cx, cy - R - 3); ctx.fillText('down', cx, cy + R + 11);

      // strip chart of the wiggle axis
      var b = fitCanvas(chartCv, 520, 170), cx2 = b.ctx, W = b.w, H = b.h, onX = wiggleOnX(st.pull);
      cx2.clearRect(0, 0, W, H);
      var vals = samples.map(function (s) { return onX ? s.x : s.y; });
      var center = rawCenter() + (function () { var t = driftVector(st.pull, st.drift); var p = onX ? t[0] : t[1]; return st.controller === 'xbox' ? Math.round(p * 327.67) : Math.round(p * 1.27); })();
      var span = st.controller === 'xbox' ? Math.max(st.n * 258 * 2.4, 800) : Math.max(st.n * 2.4, 6);
      cx2.strokeStyle = c('--lab-grid'); cx2.lineWidth = 1;
      for (var g = -2; g <= 2; g++) { var gy = H / 2 - g * (H / 4.4); cx2.beginPath(); cx2.moveTo(0, gy); cx2.lineTo(W, gy); cx2.stroke(); }
      cx2.strokeStyle = c('--lab-target'); cx2.setLineDash([4, 3]); cx2.beginPath(); cx2.moveTo(0, H / 2); cx2.lineTo(W, H / 2); cx2.stroke(); cx2.setLineDash([]);
      if (samples.length > 1) {
        var t0 = samples[0].t, t1 = samples[samples.length - 1].t, dtAll = Math.max(1, t1 - t0);
        var cols = Math.floor(W), mn = new Array(cols).fill(Infinity), mx = new Array(cols).fill(-Infinity);
        samples.forEach(function (s, i) { var k = clamp(Math.floor((s.t - t0) / dtAll * (cols - 1)), 0, cols - 1); if (vals[i] < mn[k]) mn[k] = vals[i]; if (vals[i] > mx[k]) mx[k] = vals[i]; });
        cx2.fillStyle = c('--lab-trace');
        var yOf = function (val) { return H / 2 - (val - center) / span * H; };
        for (var k2 = 0; k2 < cols; k2++) if (mn[k2] !== Infinity) { var y1 = yOf(mx[k2]), y2 = yOf(mn[k2]); cx2.fillRect(k2, Math.min(y1, y2), 1, Math.max(1.5, Math.abs(y2 - y1))); }
      }
      cx2.fillStyle = c('--lab-text'); cx2.font = '11px ' + c('--font-family-sans'); cx2.textAlign = 'left';
      cx2.fillText((onX ? 'X' : 'Y') + ' axis (wiggle axis), last 1.6 s - dashed line = drift target', 6, 13);

      // readouts
      var recent = samples.filter(function (s) { return s.t >= simT - 300; });
      var vx = recent.map(function (s) { return s.x; }), vy = recent.map(function (s) { return s.y; });
      var ro = function (id, text) { var el = $(id); if (el) el.textContent = text; };
      if (last) {
        ro('ro-x', vx.length ? (Math.min.apply(null, vx) === Math.max.apply(null, vx) ? String(vx[0]) : Math.min.apply(null, vx) + '..' + Math.max.apply(null, vx)) : '-');
        ro('ro-y', vy.length ? (Math.min.apply(null, vy) === Math.max.apply(null, vy) ? String(vy[0]) : Math.min.apply(null, vy) + '..' + Math.max.apply(null, vy)) : '-');
      }
      ro('ro-axis', onX ? 'X' : 'Y');
    }

    function frame(now) {
      if (!lastReal) lastReal = now;
      var dt = Math.min(now - lastReal, 50); lastReal = now;
      if (!st.paused && document.visibilityState === 'visible') { step(dt); draw(); }
      requestAnimationFrame(frame);
    }

    function bindSeg(id, key, parse) {
      var wrap = $(id); if (!wrap) return;
      wrap.querySelectorAll('button').forEach(function (btn) {
        btn.addEventListener('click', function () {
          wrap.querySelectorAll('button').forEach(function (o) { o.setAttribute('aria-pressed', 'false'); });
          btn.setAttribute('aria-pressed', 'true'); st[key] = parse ? parse(btn.dataset.v) : btn.dataset.v; syncUi(); rebuild();
        });
      });
    }
    function bindRange(id, key, outId, fmt) {
      var el = $(id); if (!el) return;
      el.addEventListener('input', function () { st[key] = parseInt(el.value, 10); var o = $(outId); if (o) o.textContent = fmt ? fmt(st[key]) : st[key]; rebuild(); });
    }
    function syncUi() {
      var nField = $('field-n'), rateField = $('field-rate');
      if (nField) nField.style.display = st.algo === 'off' ? 'none' : '';
      if (rateField) rateField.style.display = st.algo === 'off' ? 'none' : '';
    }
    bindSeg('seg-pull', 'pull'); bindSeg('seg-ctrl', 'controller'); bindSeg('seg-algo', 'algo'); bindSeg('seg-rate', 'rate', function (v) { return parseInt(v, 10); });
    bindRange('rng-drift', 'drift', 'out-drift', function (v) { return v + '%'; });
    bindRange('rng-n', 'n', 'out-n', function (v) { return '-' + v; });
    var pauseBtn = $('lab-pause');
    if (pauseBtn) { pauseBtn.textContent = st.paused ? 'Play' : 'Pause'; pauseBtn.addEventListener('click', function () { st.paused = !st.paused; pauseBtn.textContent = st.paused ? 'Pause'.replace('Pause', 'Play') : 'Pause'; if (!st.paused) lastReal = 0; }); }
    document.addEventListener('themechange', draw); window.addEventListener('resize', draw);
    syncUi(); rebuild(); requestAnimationFrame(frame);
    window.__stickLab = { state: st, snapshot: function () { return samples.slice(); }, rebuild: rebuild, step: step, draw: draw };
  }

  /* ---------- Pattern lab ---------- */
  function initPatternLab() {
    var lab = document.getElementById('pattern-lab'); if (!lab) return;
    var $ = function (id) { return lab.querySelector('#' + id); };
    var ta = $('pat-text'), cv = $('pat-canvas'), loopCb = $('pat-loop'), radiusRng = $('pat-radius'), play = $('pat-play');
    var steps = [], idx = 0, elapsed = 0, lastReal = 0, running = true;
    var reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (reduce) running = false;

    function reparse() {
      steps = parsePattern(ta.value); idx = 0; elapsed = 0;
      var total = steps.reduce(function (s, st) { return s + effectiveStepMs(st.ms); }, 0);
      var info = $('pat-info');
      if (info) info.textContent = steps.length ? steps.length + ' step' + (steps.length === 1 ? '' : 's') + ', one pass takes about ' + total + ' ms in the app (' + (total ? (1000 / total).toFixed(1) : '0') + ' passes per second with Loop on).' : 'No valid steps yet. Format: x:y:ms|x:y:ms|...';
      var out = $('pat-effective');
      if (out) out.textContent = steps.length ? steps.slice(0, 6).map(function (s) { return s.ms + ' ms -> ' + effectiveStepMs(s.ms) + ' ms'; }).join('  |  ') + (steps.length > 6 ? '  |  ...' : '') : '';
      draw();
    }
    var lifted = function (s) { return applyLift(s, parseInt(radiusRng.value, 10) || 0); };
    function draw() {
      var a = fitCanvas(cv, 360, 360), ctx = a.ctx, W = a.w, H = a.h, R = Math.min(W, H) / 2 - 12, cx = W / 2, cy = H / 2, c = themeVar;
      ctx.clearRect(0, 0, W, H);
      ctx.strokeStyle = c('--lab-grid'); ctx.lineWidth = 1;
      [-100, -50, 50, 100].forEach(function (g) { var p = g / 100 * R; ctx.beginPath(); ctx.moveTo(cx + p, cy - R); ctx.lineTo(cx + p, cy + R); ctx.moveTo(cx - R, cy + p); ctx.lineTo(cx + R, cy + p); ctx.stroke(); });
      ctx.strokeStyle = c('--lab-axis'); ctx.beginPath(); ctx.moveTo(cx - R, cy); ctx.lineTo(cx + R, cy); ctx.moveTo(cx, cy - R); ctx.lineTo(cx, cy + R); ctx.stroke();
      ctx.fillStyle = c('--lab-text'); ctx.font = '11px ' + c('--font-family-sans'); ctx.textAlign = 'left'; ctx.fillText('X +', cx + R - 24, cy - 4); ctx.fillText('Y +', cx + 4, cy + R - 3);
      var P = function (s) { return [cx + s.x / 100 * R, cy + s.y / 100 * R]; };
      if (!steps.length) { ctx.fillText('No steps defined', 12, 20); return; }
      var radius = parseInt(radiusRng.value, 10) || 0;
      if (radius > 0) {                       // original path (dashed) vs lifted path
        ctx.strokeStyle = c('--lab-axis'); ctx.setLineDash([3, 3]); ctx.beginPath();
        steps.forEach(function (s, i) { var p = P(s); if (i) ctx.lineTo(p[0], p[1]); else ctx.moveTo(p[0], p[1]); }); ctx.stroke(); ctx.setLineDash([]);
      }
      ctx.strokeStyle = c('--lab-trace'); ctx.lineWidth = 2; ctx.beginPath();
      steps.forEach(function (s, i) { var p = P(lifted(s)); if (i) ctx.lineTo(p[0], p[1]); else ctx.moveTo(p[0], p[1]); }); ctx.stroke();
      steps.forEach(function (s, i) {
        var p = P(lifted(s)); ctx.fillStyle = i === 0 ? c('--color-success') : c('--color-danger'); ctx.beginPath(); ctx.arc(p[0], p[1], 3.5, 0, 7); ctx.fill();
        if (steps.length <= 24) { ctx.fillStyle = c('--lab-text'); ctx.fillText(String(i), p[0] + 6, p[1] - 6); }
      });
      var cur = P(lifted(steps[Math.min(idx, steps.length - 1)]));
      ctx.fillStyle = c('--lab-dot'); ctx.beginPath(); ctx.arc(cur[0], cur[1], 6, 0, 7); ctx.fill();
      var st = $('pat-status'); if (st) st.textContent = 'step ' + (Math.min(idx, steps.length - 1) + 1) + '/' + steps.length;
    }
    function frame(now) {
      if (!lastReal) lastReal = now;
      var dt = Math.min(now - lastReal, 80); lastReal = now;
      if (running && steps.length && document.visibilityState === 'visible') {
        elapsed += dt;
        var d = effectiveStepMs(steps[Math.min(idx, steps.length - 1)].ms);
        while (elapsed >= d) {
          elapsed -= d;
          if (idx >= steps.length - 1) { if (loopCb.checked) idx = 0; else { idx = steps.length - 1; elapsed = 0; break; } } else idx++;
          d = effectiveStepMs(steps[idx].ms);
        }
        draw();
      }
      requestAnimationFrame(frame);
    }
    lab.querySelectorAll('[data-preset]').forEach(function (b) {
      b.addEventListener('click', function () {
        var map = {
          circle: circlePreset(),
          vertical: '0:0:17.3|0:18:17.3|2:32:17.3|4:44:17.3|5:50:17.3|4:48:17.3|2:44:17.3',
          square: '30:0:600|0:30:600|-30:0:600|0:-30:600'
        };
        ta.value = map[b.dataset.preset]; loopCb.checked = b.dataset.preset !== 'vertical'; reparse();
      });
    });
    ta.addEventListener('input', reparse); loopCb.addEventListener('change', function () { idx = 0; elapsed = 0; });
    radiusRng.addEventListener('input', function () { var o = $('pat-radius-out'); if (o) o.textContent = radiusRng.value + '%'; draw(); });
    if (play) { play.textContent = running ? 'Pause' : 'Play'; play.addEventListener('click', function () { running = !running; play.textContent = running ? 'Pause' : 'Play'; if (running) lastReal = 0; }); }
    document.addEventListener('themechange', draw); window.addEventListener('resize', draw);
    reparse(); requestAnimationFrame(frame);
    window.__patternLab = { reparse: reparse, steps: function () { return steps.slice(); } };
  }
})();
