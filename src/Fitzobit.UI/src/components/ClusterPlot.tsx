import { forwardRef, useCallback, useEffect, useImperativeHandle, useRef, useState } from 'react';
import type { Boundary } from '../api';
import { groupColor, groupLabel } from './groups';

export interface PlotPoint {
  id: number;
  lat: number;
  lng: number;
  group: number | null;
  approximate: boolean;
  /** Short label drawn next to the point when zoomed in (CustomerID). */
  label: string;
  title: string;
  lines: string[];
}

export interface PlotGroup {
  number: number;
  boundary: Boundary;
  centerLat: number | null;
  centerLng: number | null;
  /** Main areas of the group, drawn under its label (e.g. "RS Puram · Gandhipuram"). */
  areas?: string[];
}

export interface ClusterPlotHandle {
  fit: () => void;
  zoom: (factor: number) => void;
  focus: (id: number) => void;
}

type SelectMode = 'replace' | 'add';

/** Plot margins for the axes (CSS px). */
const M = { left: 64, right: 12, top: 12, bottom: 40 };
const POINT_RADIUS = 4.5;
const HIT_RADIUS = 9;
const MAX_POINT_LABELS = 150;
/** Group centres are red stars, as in a classic K-Means plot. */
const CENTRE_COLOR = '#e03131';

interface ViewState {
  cx: number; // centre, projected units (x = lng·cos(lat0), y = lat)
  cy: number;
  scale: number; // CSS px per projected unit
}

/**
 * Customers on a longitude (X) / latitude (Y) plot, coloured by delivery group, with group outlines. Drawn on a canvas
 * so thousands of points stay fast; labels appear only when few points are in view. Scroll to zoom, drag to pan,
 * click to select (Ctrl/Shift adds), Shift+drag or the select tool to box-select.
 */
const ClusterPlot = forwardRef<ClusterPlotHandle, {
  points: PlotPoint[];
  groups: PlotGroup[];
  selected: ReadonlySet<number>;
  highlightId: number | null;
  showLabels: boolean;
  showBoundaries: boolean;
  /** Star at each group's centre (the K-Means centroid for a suggestion), as in a classic cluster plot. */
  showCentres: boolean;
  boxSelect: boolean;
  hiddenGroups: ReadonlySet<number | 'none'>;
  onSelect: (ids: number[], mode: SelectMode) => void;
}>(function ClusterPlot({ points, groups, selected, highlightId, showLabels, showBoundaries, showCentres, boxSelect, hiddenGroups, onSelect }, ref) {
  const wrapRef = useRef<HTMLDivElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const view = useRef<ViewState | null>(null);
  const size = useRef({ w: 0, h: 0 });
  const screen = useRef<Float64Array>(new Float64Array(0));
  const drag = useRef<{ x: number; y: number; moved: boolean; box: boolean; view: ViewState; additive: boolean } | null>(null);
  const [box, setBox] = useState<{ x0: number; y0: number; x1: number; y1: number } | null>(null);
  const [hover, setHover] = useState<{ index: number; x: number; y: number } | null>(null);
  const frame = useRef(0);
  /** True once the user zoomed or panned; until then the plot keeps fitting all customers (e.g. after a resize). */
  const interacted = useRef(false);

  // Equal distances on both axes: one degree of longitude is cos(latitude) of a degree of latitude.
  const lat0 = points.length > 0 ? points.reduce((s, p) => s + p.lat, 0) / points.length : 11;
  const kx = Math.cos((lat0 * Math.PI) / 180);
  const visible = useCallback((p: PlotPoint) => !hiddenGroups.has(p.group ?? 'none'), [hiddenGroups]);

  const fit = useCallback(() => {
    const { w, h } = size.current;
    if (w === 0) return;
    const shown = points.filter(visible);
    const source = shown.length > 0 ? shown : points;
    if (source.length === 0) {
      view.current = { cx: 77 * kx, cy: 11, scale: 2000 };
      return;
    }
    let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
    for (const p of source) {
      const x = p.lng * kx;
      minX = Math.min(minX, x); maxX = Math.max(maxX, x);
      minY = Math.min(minY, p.lat); maxY = Math.max(maxY, p.lat);
    }
    const pw = w - M.left - M.right - 120; // room for labels at the edges
    const ph = h - M.top - M.bottom - 60;
    const dx = Math.max(maxX - minX, 0.002);
    const dy = Math.max(maxY - minY, 0.002);
    view.current = { cx: (minX + maxX) / 2, cy: (minY + maxY) / 2, scale: Math.min(pw / dx, ph / dy) };
  }, [points, kx, visible]);

  const toScreen = (lat: number, lng: number, v: ViewState) => {
    const { w, h } = size.current;
    const midX = M.left + (w - M.left - M.right) / 2;
    const midY = M.top + (h - M.top - M.bottom) / 2;
    return [midX + (lng * kx - v.cx) * v.scale, midY - (lat - v.cy) * v.scale] as const;
  };

  const draw = useCallback(() => {
    const canvas = canvasRef.current;
    const v = view.current;
    if (!canvas || !v) return;
    const { w, h } = size.current;
    const dpr = window.devicePixelRatio || 1;
    const ctx = canvas.getContext('2d')!;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, h);

    const plotW = w - M.left - M.right;
    const plotH = h - M.top - M.bottom;
    const css = getComputedStyle(canvas);
    const muted = css.getPropertyValue('--muted').trim() || '#64748b';
    const border = css.getPropertyValue('--border').trim() || '#e3e8f1';
    const text = css.getPropertyValue('--text').trim() || '#0f172a';
    const surface = css.getPropertyValue('--surface').trim() || '#ffffff';
    const textColor = css.getPropertyValue('--text-2').trim() || '#334155';

    // Axes and grid, in degrees.
    const midX = M.left + plotW / 2;
    const midY = M.top + plotH / 2;
    const lngAt = (sx: number) => (v.cx + (sx - midX) / v.scale) / kx;
    const latAt = (sy: number) => v.cy - (sy - midY) / v.scale;
    const lngMin = lngAt(M.left), lngMax = lngAt(M.left + plotW);
    const latMin = latAt(M.top + plotH), latMax = latAt(M.top);
    ctx.font = '11px Inter, system-ui, sans-serif';
    ctx.lineWidth = 1;
    const ticks = (min: number, max: number, pixels: number) => {
      const raw = (max - min) / Math.max(2, Math.floor(pixels / 90));
      const pow = Math.pow(10, Math.floor(Math.log10(raw)));
      const step = [1, 2, 5, 10].map((m) => m * pow).find((s) => s >= raw) ?? raw;
      const decimals = Math.max(0, -Math.floor(Math.log10(step)));
      const list: { value: number; text: string }[] = [];
      for (let t = Math.ceil(min / step) * step; t <= max; t += step) list.push({ value: t, text: t.toFixed(decimals) });
      return list;
    };
    ctx.strokeStyle = border;
    ctx.fillStyle = muted;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'top';
    for (const t of ticks(lngMin, lngMax, plotW)) {
      const [sx] = toScreen(v.cy, t.value, v);
      ctx.beginPath(); ctx.moveTo(sx, M.top); ctx.lineTo(sx, M.top + plotH); ctx.stroke();
      ctx.fillText(t.text, sx, M.top + plotH + 6);
    }
    ctx.textAlign = 'right';
    ctx.textBaseline = 'middle';
    for (const t of ticks(latMin, latMax, plotH)) {
      const [, sy] = toScreen(t.value, v.cx / kx, v);
      ctx.beginPath(); ctx.moveTo(M.left, sy); ctx.lineTo(M.left + plotW, sy); ctx.stroke();
      ctx.fillText(t.text, M.left - 8, sy);
    }
    ctx.textAlign = 'center';
    ctx.textBaseline = 'bottom';
    ctx.fillText('Longitude', M.left + plotW / 2, h - 2);
    ctx.save();
    ctx.translate(12, M.top + plotH / 2);
    ctx.rotate(-Math.PI / 2);
    ctx.textBaseline = 'top';
    ctx.fillText('Latitude', 0, 0);
    ctx.restore();

    ctx.save();
    ctx.beginPath();
    ctx.rect(M.left, M.top, plotW, plotH);
    ctx.clip();

    // Group outlines: hull for 3+ locations, a dashed line for 2, nothing for 1.
    if (showBoundaries)
      for (const g of groups) {
        if (hiddenGroups.has(g.number) || g.boundary.kind === 'none' || g.boundary.kind === 'point') continue;
        const color = groupColor(g.number);
        ctx.beginPath();
        g.boundary.points.forEach(([lat, lng], i) => {
          const [sx, sy] = toScreen(lat, lng, v);
          if (i === 0) ctx.moveTo(sx, sy);
          else ctx.lineTo(sx, sy);
        });
        ctx.strokeStyle = color;
        ctx.lineWidth = 1.5;
        if (g.boundary.kind === 'polygon') {
          ctx.closePath();
          ctx.globalAlpha = 0.13;
          ctx.fillStyle = color;
          ctx.fill();
          ctx.globalAlpha = 0.85;
          ctx.setLineDash([]);
        } else {
          ctx.globalAlpha = 0.7;
          ctx.setLineDash([5, 4]);
        }
        ctx.stroke();
        ctx.setLineDash([]);
        ctx.globalAlpha = 1;
      }

    // Points (screen positions kept for hit testing).
    const pos = new Float64Array(points.length * 2);
    let inView = 0;
    points.forEach((p, i) => {
      const [sx, sy] = toScreen(p.lat, p.lng, v);
      pos[i * 2] = sx;
      pos[i * 2 + 1] = sy;
      if (!visible(p)) { pos[i * 2] = NaN; return; }
      if (sx < M.left - 10 || sx > M.left + plotW + 10 || sy < M.top - 10 || sy > M.top + plotH + 10) return;
      inView++;
      const color = groupColor(p.group);
      ctx.beginPath();
      ctx.arc(sx, sy, POINT_RADIUS, 0, Math.PI * 2);
      if (p.approximate) {
        ctx.fillStyle = surface;
        ctx.fill();
        ctx.lineWidth = 1.8;
        ctx.strokeStyle = color;
        ctx.stroke();
      } else {
        ctx.fillStyle = color;
        ctx.fill();
      }
      if (selected.has(p.id)) {
        ctx.beginPath();
        ctx.arc(sx, sy, POINT_RADIUS + 3, 0, Math.PI * 2);
        ctx.lineWidth = 2;
        ctx.strokeStyle = text;
        ctx.stroke();
      }
    });
    screen.current = pos;

    // Search highlight.
    const hi = highlightId == null ? -1 : points.findIndex((p) => p.id === highlightId);
    if (hi >= 0 && !Number.isNaN(pos[hi * 2])) {
      ctx.beginPath();
      ctx.arc(pos[hi * 2], pos[hi * 2 + 1], 13, 0, Math.PI * 2);
      ctx.lineWidth = 3;
      ctx.strokeStyle = '#f59e0b';
      ctx.stroke();
    }

    if (showCentres)
      for (const g of groups) {
        if (hiddenGroups.has(g.number) || g.centerLat == null || g.centerLng == null) continue;
        const [sx, sy] = toScreen(g.centerLat, g.centerLng, v);
        star(ctx, sx, sy, 12, 5);
        ctx.lineWidth = 4; // white halo keeps the star visible on top of its own cluster
        ctx.strokeStyle = surface;
        ctx.stroke();
        ctx.fillStyle = CENTRE_COLOR;
        ctx.fill();
        ctx.lineWidth = 1;
        ctx.strokeStyle = '#7f1d1d';
        ctx.stroke();
      }

    if (showLabels) {
      ctx.font = '600 11px Inter, system-ui, sans-serif';
      ctx.lineWidth = 3;
      ctx.strokeStyle = surface;
      ctx.textAlign = 'left';
      ctx.textBaseline = 'middle';
      if (inView <= MAX_POINT_LABELS)
        points.forEach((p, i) => {
          const sx = pos[i * 2], sy = pos[i * 2 + 1];
          if (Number.isNaN(sx) || sx < M.left || sx > M.left + plotW || sy < M.top || sy > M.top + plotH) return;
          ctx.fillStyle = groupColor(p.group);
          ctx.strokeText(p.label, sx + 7, sy);
          ctx.fillText(p.label, sx + 7, sy);
        });
      ctx.font = '700 13px Inter, system-ui, sans-serif';
      ctx.textAlign = 'center';
      for (const g of groups) {
        if (hiddenGroups.has(g.number) || g.centerLat == null || g.centerLng == null) continue;
        const [sx, sy] = toScreen(g.centerLat, g.centerLng, v);
        // A solid tag above the centre star, so the group name and its areas stay readable over dense points.
        const color = groupColor(g.number);
        const title = groupLabel(g.number);
        const areas = areaLine(g.areas);
        ctx.font = '700 13px Inter, system-ui, sans-serif';
        const titleWidth = ctx.measureText(title).width;
        ctx.font = '600 11.5px Inter, system-ui, sans-serif';
        const areaWidth = areas ? ctx.measureText(areas).width : 0;
        const boxW = Math.max(titleWidth, areaWidth) + 14;
        const boxH = areas ? 36 : 21;
        const bottom = sy - (showCentres ? 16 : 10);
        const left = sx - boxW / 2, top = bottom - boxH;
        ctx.save();
        ctx.globalAlpha = 0.93;
        ctx.fillStyle = surface;
        ctx.beginPath();
        ctx.roundRect(left, top, boxW, boxH, 6);
        ctx.fill();
        ctx.globalAlpha = 1;
        ctx.strokeStyle = color;
        ctx.lineWidth = 1.5;
        ctx.stroke();
        ctx.restore();
        ctx.fillStyle = color;
        ctx.font = '700 13px Inter, system-ui, sans-serif';
        ctx.fillText(title, sx, top + 11);
        if (areas) {
          ctx.font = '600 11.5px Inter, system-ui, sans-serif';
          ctx.fillStyle = textColor;
          ctx.fillText(areas, sx, top + 26);
        }
      }
    }
    ctx.restore();

    ctx.strokeStyle = border;
    ctx.lineWidth = 1;
    ctx.strokeRect(M.left + 0.5, M.top + 0.5, plotW, plotH);
  }, [points, groups, selected, highlightId, showLabels, showBoundaries, showCentres, hiddenGroups, kx, visible]); // eslint-disable-line react-hooks/exhaustive-deps

  const requestDraw = useCallback(() => {
    cancelAnimationFrame(frame.current);
    frame.current = requestAnimationFrame(draw);
  }, [draw]);

  // Size the canvas to its container (and the device pixel ratio).
  useEffect(() => {
    const wrap = wrapRef.current!;
    const resize = () => {
      const rect = wrap.getBoundingClientRect();
      const dpr = window.devicePixelRatio || 1;
      size.current = { w: rect.width, h: rect.height };
      const canvas = canvasRef.current!;
      canvas.width = Math.round(rect.width * dpr);
      canvas.height = Math.round(rect.height * dpr);
      if (!view.current || !interacted.current) fit();
      requestDraw();
    };
    const observer = new ResizeObserver(resize);
    observer.observe(wrap);
    resize();
    return () => observer.disconnect();
  }, [fit, requestDraw]);

  // Fit when a different set of customers is shown (not on every selection change).
  const pointsKey = `${points.length}:${points[0]?.id ?? ''}:${points[points.length - 1]?.id ?? ''}`;
  useEffect(() => {
    interacted.current = false;
    fit();
    requestDraw();
  }, [pointsKey]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(requestDraw, [requestDraw]);

  const zoomAt = useCallback((factor: number, sx?: number, sy?: number) => {
    const v = view.current;
    if (!v) return;
    const { w, h } = size.current;
    const midX = M.left + (w - M.left - M.right) / 2;
    const midY = M.top + (h - M.top - M.bottom) / 2;
    const px = sx ?? midX, py = sy ?? midY;
    // Keep the point under the cursor fixed.
    const ux = v.cx + (px - midX) / v.scale;
    const uy = v.cy - (py - midY) / v.scale;
    interacted.current = true;
    const scale = Math.min(5e6, Math.max(50, v.scale * factor));
    view.current = { scale, cx: ux - (px - midX) / scale, cy: uy + (py - midY) / scale };
    requestDraw();
  }, [requestDraw]);

  useImperativeHandle(ref, () => ({
    fit: () => { interacted.current = false; fit(); requestDraw(); },
    zoom: (factor) => zoomAt(factor),
    focus: (id) => {
      const p = points.find((x) => x.id === id);
      const v = view.current;
      if (!p || !v) return;
      interacted.current = true;
      view.current = { cx: p.lng * kx, cy: p.lat, scale: Math.max(v.scale, 40000) };
      requestDraw();
    },
  }), [fit, requestDraw, zoomAt, points, kx]);

  useEffect(() => {
    const canvas = canvasRef.current!;
    const onWheel = (e: WheelEvent) => {
      e.preventDefault();
      const rect = canvas.getBoundingClientRect();
      zoomAt(e.deltaY < 0 ? 1.2 : 1 / 1.2, e.clientX - rect.left, e.clientY - rect.top);
    };
    canvas.addEventListener('wheel', onWheel, { passive: false });
    return () => canvas.removeEventListener('wheel', onWheel);
  }, [zoomAt]);

  const nearest = (x: number, y: number) => {
    const pos = screen.current;
    let best = -1, bestD = HIT_RADIUS * HIT_RADIUS;
    for (let i = 0; i < pos.length / 2; i++) {
      const dx = pos[i * 2] - x, dy = pos[i * 2 + 1] - y;
      const d = dx * dx + dy * dy;
      if (d <= bestD) { best = i; bestD = d; }
    }
    return best;
  };

  const local = (e: React.PointerEvent) => {
    const rect = canvasRef.current!.getBoundingClientRect();
    return { x: e.clientX - rect.left, y: e.clientY - rect.top };
  };

  const onPointerDown = (e: React.PointerEvent) => {
    if (!view.current) return;
    (e.target as Element).setPointerCapture(e.pointerId);
    const { x, y } = local(e);
    drag.current = { x, y, moved: false, box: boxSelect || e.shiftKey, view: { ...view.current }, additive: e.shiftKey || e.ctrlKey || e.metaKey };
    setHover(null);
  };

  const onPointerMove = (e: React.PointerEvent) => {
    const { x, y } = local(e);
    const d = drag.current;
    if (!d) {
      const i = nearest(x, y);
      setHover(i >= 0 ? { index: i, x, y } : null);
      return;
    }
    if (Math.abs(x - d.x) + Math.abs(y - d.y) > 3) d.moved = true;
    if (d.box) setBox({ x0: d.x, y0: d.y, x1: x, y1: y });
    else if (d.moved) {
      interacted.current = true;
      view.current = { ...d.view, cx: d.view.cx - (x - d.x) / d.view.scale, cy: d.view.cy + (y - d.y) / d.view.scale };
      requestDraw();
    }
  };

  const onPointerUp = (e: React.PointerEvent) => {
    const d = drag.current;
    drag.current = null;
    if (!d) return;
    const { x, y } = local(e);
    if (d.box && d.moved) {
      const [x0, x1] = [Math.min(d.x, x), Math.max(d.x, x)];
      const [y0, y1] = [Math.min(d.y, y), Math.max(d.y, y)];
      const pos = screen.current;
      const ids = points.filter((_, i) => pos[i * 2] >= x0 && pos[i * 2] <= x1 && pos[i * 2 + 1] >= y0 && pos[i * 2 + 1] <= y1).map((p) => p.id);
      onSelect(ids, d.additive ? 'add' : 'replace');
    } else if (!d.moved) {
      const i = nearest(x, y);
      if (i >= 0) onSelect([points[i].id], d.additive ? 'add' : 'replace');
      else if (!d.additive) onSelect([], 'replace');
    }
    setBox(null);
  };

  const hovered = hover ? points[hover.index] : null;
  return (
    <div className={`cluster-plot ${boxSelect ? 'is-box-select' : ''}`} ref={wrapRef}>
      <canvas
        ref={canvasRef}
        role="img"
        aria-label={`Delivery map: ${points.length} customers plotted by longitude and latitude`}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onPointerLeave={() => !drag.current && setHover(null)}
      />
      {box && (
        <div
          className="plot-box"
          style={{ left: Math.min(box.x0, box.x1), top: Math.min(box.y0, box.y1), width: Math.abs(box.x1 - box.x0), height: Math.abs(box.y1 - box.y0) }}
        />
      )}
      {hovered && hover && (
        <div className="plot-tooltip" style={{ left: hover.x + 14, top: hover.y + 14 }} role="tooltip">
          <div className="plot-tooltip-title">
            <span className="swatch" style={{ background: groupColor(hovered.group) }} /> {hovered.title}
          </div>
          {hovered.lines.map((l) => (
            <div key={l}>{l}</div>
          ))}
        </div>
      )}
    </div>
  );
});

/** "RS Puram · Gandhipuram · Avinashi Road", shortened so a label never covers half the plot. */
function areaLine(areas: string[] | undefined): string {
  if (!areas || areas.length === 0) return '';
  const text = areas.join(' · ');
  return text.length > 48 ? `${text.slice(0, 46).trimEnd()}…` : text;
}

/** Five-pointed star path centred on (x, y). */
function star(ctx: CanvasRenderingContext2D, x: number, y: number, outer: number, inner: number) {
  ctx.beginPath();
  for (let i = 0; i < 10; i++) {
    const r = i % 2 === 0 ? outer : inner;
    const a = -Math.PI / 2 + (i * Math.PI) / 5;
    if (i === 0) ctx.moveTo(x + r * Math.cos(a), y + r * Math.sin(a));
    else ctx.lineTo(x + r * Math.cos(a), y + r * Math.sin(a));
  }
  ctx.closePath();
}

export default ClusterPlot;
