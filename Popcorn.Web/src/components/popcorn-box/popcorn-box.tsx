import React, { useRef, useState } from 'react';
import './popcorn-box.css';

type Particle = {
  id: number;
  x: number;
  y: number;
  vx: number;
  vy: number;
  rot: number;
};

const GRAVITY = 3000; // px / s²

const PopcornBox: React.FC = () => {
  const [particles, setParticles] = useState<Particle[]>([]);
  const iconRef = useRef<HTMLDivElement>(null);

  const shake = () => {
    const el = iconRef.current?.querySelector('.shake-wrapper');
    if (!el) return;

    el.classList.remove('shake');
    void (el as HTMLElement).offsetWidth; // reflow
    el.classList.add('shake');
  };

  const spawn = (e: React.MouseEvent) => {
    shake();

    const rect = iconRef.current!.getBoundingClientRect();
    const localX = e.clientX - rect.left;
    const localY = e.clientY - rect.top;

    const fromTop = localY > rect.height * 0.4;

    const centerX = rect.width * 0.5;
    const spawnWidth = rect.width * 0.35;

    const rand = () => crypto.getRandomValues(new Uint32Array(1))[0] / 2 ** 32;

    const x = fromTop ? centerX + (rand() - 0.5) * spawnWidth : localX;
    const y = fromTop ? 0 : localY;

    const p: Particle = {
      id: performance.now(),
      x,
      y,
      vx: (rand() - 0.5) * (fromTop ? 300 : 600),
      vy: -900 - rand() * 200,
      rot: rand() * 360,
    };

    setParticles((ps) => [...ps, p]);
  };

  // physics loop
  React.useEffect(() => {
    let last = performance.now();

    const tick = (now: number) => {
      const dt = (now - last) / 1000;
      last = now;

      setParticles(
        (ps) =>
          ps
            .map((p) => ({
              ...p,
              x: p.x + p.vx * dt,
              y: p.y + p.vy * dt,
              vy: p.vy + GRAVITY * dt,
            }))
            .filter((p) => p.y < 600) // remove when below ground
      );

      requestAnimationFrame(tick);
    };

    const id = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(id);
  }, []);

  return (
    <div ref={iconRef} className="icon-wrapper" onClick={spawn}>
      <div className="shake-wrapper">
        <img src="images/popcorn.ico" className="pulsate" />
      </div>

      {particles.map((p) => (
        <img
          key={p.id}
          src="images/pop_1.png"
          className="particle"
          style={{
            left: `${p.x}px`,
            top: `${p.y}px`,
            transform: `rotate(${p.rot}deg)`,
          }}
        />
      ))}
    </div>
  );
};

export default PopcornBox;
