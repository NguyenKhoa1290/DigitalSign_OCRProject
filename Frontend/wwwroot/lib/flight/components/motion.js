import { Spring } from '../spring.js';

// Shared press springs from DampedDragAnimation.kt; updates stop when settled.
export class PressMotion {
  constructor(update, scale = 1.5, stiffness = 1000, damping = 1) {
    this.update = update; this.pressedScale = scale;
    this.progress = new Spring(0, stiffness, damping);
    this.scaleX = new Spring(1, 250, .6);
    this.scaleY = new Spring(1, 250, .7);
    this.springs = [this.progress, this.scaleX, this.scaleY];
  }
  press() { this.progress.target = 1; this.scaleX.target = this.scaleY.target = this.pressedScale; this.wake(); }
  release() { this.progress.target = 0; this.scaleX.target = this.scaleY.target = 1; this.wake(); }
  wake() {
    if (this.frame) return;
    this.last = performance.now();
    const tick = time => {
      this.frame = 0;
      const dt = Math.min(.05, Math.max(.001, (time - this.last) / 1000)); this.last = time;
      const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
      for (const spring of this.springs) reduced ? spring.snap(spring.target) : spring.step(dt);
      this.update(this);
      if (!this.springs.every(spring => spring.settled)) this.frame = requestAnimationFrame(tick);
    };
    this.frame = requestAnimationFrame(tick);
  }
  dispose() { cancelAnimationFrame(this.frame); this.frame = 0; }
}
