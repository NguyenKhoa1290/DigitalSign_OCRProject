// Copyright 2025 Kyant. Apache-2.0. Web adaptation of DampedDragAnimation.kt.
// Unit-mass damped spring; exact integration avoids frame-rate-dependent motion.
export class Spring {
  constructor(value, stiffness, dampingRatio, threshold = 0.001) {
    this.value = value;
    this.target = value;
    this.velocity = 0;
    this.stiffness = stiffness;
    this.dampingRatio = dampingRatio;
    this.threshold = threshold;
  }

  snap(value) {
    this.value = this.target = value;
    this.velocity = 0;
  }

  get settled() {
    return Math.abs(this.value - this.target) < this.threshold &&
      Math.abs(this.velocity) < this.threshold * 10;
  }

  step(dt) {
    if (this.settled) {
      this.snap(this.target);
      return;
    }
    const w = Math.sqrt(this.stiffness);
    const decay = this.dampingRatio * w;
    const displacement = this.value - this.target;
    const initialVelocity = this.velocity;
    const envelope = Math.exp(-decay * dt);
    if (this.dampingRatio < 1) {
      const frequency = w * Math.sqrt(1 - this.dampingRatio ** 2);
      const a = displacement;
      const b = (initialVelocity + decay * a) / frequency;
      const cos = Math.cos(frequency * dt);
      const sin = Math.sin(frequency * dt);
      const offset = a * cos + b * sin;
      this.value = this.target + envelope * offset;
      this.velocity = envelope * (-decay * offset + frequency * (b * cos - a * sin));
    } else {
      const b = initialVelocity + w * displacement;
      this.value = this.target + envelope * (displacement + b * dt);
      this.velocity = envelope * (initialVelocity - w * b * dt);
    }
  }
}

export const clamp = (value, min, max) => Math.max(min, Math.min(max, value));
