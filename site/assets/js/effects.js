const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

function setupReveals() {
  const targets = document.querySelectorAll("[data-reveal]");
  if (!targets.length || reducedMotion.matches || !("IntersectionObserver" in window)) return;

  const observer = new IntersectionObserver((entries) => {
    for (const entry of entries) {
      if (entry.isIntersecting) {
        entry.target.classList.add("is-visible");
        observer.unobserve(entry.target);
      }
    }
  }, { rootMargin: "0px 0px -8% 0px", threshold: 0.08 });

  for (const target of targets) {
    if (target.getBoundingClientRect().top < window.innerHeight) {
      target.classList.add("is-visible");
    }
    else {
      observer.observe(target);
    }
  }
  document.documentElement.classList.add("motion-ready");
}

function setupCompare(figure) {
  const media = figure.querySelector(".compare-media");
  const range = figure.querySelector("[data-compare-range]");
  if (!media || !range) return;

  const setSplit = (value) => {
    const clamped = Math.min(100, Math.max(0, value));
    media.style.setProperty("--split", `${clamped}%`);
    range.value = String(Math.round(clamped));
  };
  const splitFromPointer = (event) => {
    const bounds = media.getBoundingClientRect();
    setSplit(((event.clientX - bounds.left) / bounds.width) * 100);
  };

  let cancelIntro = () => {};
  range.addEventListener("input", () => {
    cancelIntro();
    setSplit(Number(range.value));
  });
  media.addEventListener("pointerdown", (event) => {
    if (event.button !== 0 || event.target.closest("a, aside")) return;
    event.preventDefault();
    cancelIntro();
    media.setPointerCapture(event.pointerId);
    media.classList.add("is-dragging");
    splitFromPointer(event);
  });
  media.addEventListener("pointermove", (event) => {
    if (media.classList.contains("is-dragging")) splitFromPointer(event);
  });
  // Native image dragging would otherwise cancel the pointer stream.
  media.addEventListener("dragstart", (event) => event.preventDefault());
  const endDrag = () => media.classList.remove("is-dragging");
  media.addEventListener("pointerup", endDrag);
  media.addEventListener("pointercancel", endDrag);

  if (figure.hasAttribute("data-compare-intro") && !reducedMotion.matches) {
    cancelIntro = playIntro(setSplit);
  }
}

// A single slow sweep tells visitors the frame is interactive.
function playIntro(setSplit) {
  const keyframes = [[0, 50], [900, 50], [2100, 82], [3500, 22], [4600, 50]];
  const duration = keyframes.at(-1)[0];
  const ease = (t) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);
  let frame = 0;
  let start = 0;

  const step = (now) => {
    start ||= now;
    const elapsed = Math.min(now - start, duration);
    const index = keyframes.findIndex(([time]) => time >= elapsed);
    const [t1, v1] = keyframes[Math.max(index, 1)];
    const [t0, v0] = keyframes[Math.max(index, 1) - 1];
    setSplit(v0 + (v1 - v0) * ease((elapsed - t0) / (t1 - t0 || 1)));
    if (elapsed < duration) frame = requestAnimationFrame(step);
  };
  frame = requestAnimationFrame(step);
  return () => cancelAnimationFrame(frame);
}

document.documentElement.classList.add("js");
setupReveals();
for (const figure of document.querySelectorAll("[data-compare]")) {
  setupCompare(figure);
}
