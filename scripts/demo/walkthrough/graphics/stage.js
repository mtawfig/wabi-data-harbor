/* Beat control for the graphic scenes: record.mjs calls window.showBeat(n) when narration line n starts. */
window.showBeat = function (n) {
  document.querySelectorAll('[data-beat]').forEach(function (el) {
    var from = +el.dataset.beat, until = el.dataset.until === undefined ? Infinity : +el.dataset.until;
    var visible = n >= from && n < until;
    el.classList.toggle('in', visible);
    el.classList.toggle('out', !visible && n >= until);
  });
  document.body.dataset.step = n;
  if (window.onBeat) window.onBeat(n);
};
/* Stagger: children of [data-stagger] get --i = their index. */
document.querySelectorAll('[data-stagger]').forEach(function (group) {
  Array.prototype.forEach.call(group.children, function (child, i) { child.style.setProperty('--i', i); });
});
