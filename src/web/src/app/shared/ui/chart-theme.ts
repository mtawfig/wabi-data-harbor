import type { EChartsOption } from 'echarts';

/** Brand chart palette and shared defaults (blue/aqua, readable labels), used by every dashboard chart. */
export const CHART_COLORS = ['#087DF2', '#22B8CF', '#0B1F3A', '#F59E0B', '#DC2626', '#64748B'];

export function baseChartOption(rtl: boolean): EChartsOption {
  return {
    color: CHART_COLORS,
    animation: false,
    textStyle: { fontFamily: rtl ? "'IBM Plex Sans Arabic', Inter, sans-serif" : 'Inter, system-ui, sans-serif', color: '#334155' },
    grid: { left: rtl ? 24 : 48, right: rtl ? 48 : 24, top: 36, bottom: 36, containLabel: false },
    tooltip: { trigger: 'axis', backgroundColor: '#0B1F3A', borderWidth: 0, textStyle: { color: '#F5F8FC' } },
    legend: { top: 0, textStyle: { color: '#334155' }, [rtl ? 'right' : 'left']: 0 },
    xAxis: { type: 'category', inverse: rtl, axisLine: { lineStyle: { color: '#E2E8F0' } }, axisTick: { show: false }, axisLabel: { color: '#64748B' } },
    yAxis: { type: 'value', minInterval: 1, splitLine: { lineStyle: { color: '#E2E8F0' } }, axisLabel: { color: '#64748B' }, position: rtl ? 'right' : 'left' },
  };
}
