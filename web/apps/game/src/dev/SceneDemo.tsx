import { prefersReducedMotion, startRenderLoop } from '@nobodyfalls/render';
import { useEffect, useRef, useState } from 'react';
import { createReferenceDemo, type ReferenceDemo } from './referenceDemo.ts';
import styles from './SceneDemo.module.css';

function untilMidnight(now: Date): string {
  const midnight = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + 1);
  const left = Math.max(0, Math.floor((midnight - now.getTime()) / 1000));
  const pad = (n: number): string => String(n).padStart(2, '0');
  return `${pad(Math.floor(left / 3600))}:${pad(Math.floor((left % 3600) / 60))}:${pad(left % 60)}`;
}

/**
 * `/dev/scene`: the reference scene rebuilt with the production renderers, to compare
 * side by side with docs/reference/scene-demo.dc.html. Texts are the reference's own
 * (French), so this page is not translated.
 */
export function SceneDemo() {
  const canvas = useRef<HTMLCanvasElement>(null);
  const demo = useRef<ReferenceDemo | null>(null);
  const lastT = useRef(0);
  const [rescued, setRescued] = useState(false);
  const [countdown, setCountdown] = useState(() => untilMidnight(new Date()));

  useEffect(() => {
    const element = canvas.current;
    if (!element) {
      return undefined;
    }
    const scene = createReferenceDemo();
    demo.current = scene;
    return startRenderLoop(
      element,
      (frame) => {
        lastT.current = frame.t;
        scene.draw(frame);
      },
      {
        reducedMotion: prefersReducedMotion(),
        onResize: (w, h, ratio) => {
          scene.resize(w, h, ratio);
        },
      },
    );
  }, []);

  useEffect(() => {
    const timer = setInterval(() => {
      setCountdown(untilMidnight(new Date()));
    }, 1000);
    return () => {
      clearInterval(timer);
    };
  }, []);

  const rescue = (): void => {
    demo.current?.rescue(lastT.current, prefersReducedMotion());
    setRescued(true);
  };

  return (
    <div className={styles.stage}>
      <canvas
        ref={canvas}
        className={styles.canvas}
        role="img"
        aria-label={
          rescued
            ? 'Tu es suspendu au-dessus du vide, retenu par les cordes de léa_m et kenji. Tu tiens tom.b, ines et maintenant yuki, qui a attrapé ta corde.'
            : 'Tu es suspendu au-dessus du vide, retenu par les cordes de léa_m et kenji. Tu tiens tom.b et ines. Yuki ne tient plus qu’à un fil effiloché.'
        }
      />
      <header className={styles.header}>
        <span className={styles.brand}>NOBODYFALLS</span>
        <span className={styles.night}>NUIT 41 · {countdown}</span>
      </header>

      <span className={styles.name} style={{ left: 124, top: 146 }}>
        LÉA_M
      </span>
      <span className={styles.name} style={{ left: 226, top: 122 }}>
        KENJI
      </span>
      <span className={`${styles.name} ${styles.me}`} style={{ left: 232, top: 296 }}>
        TOI
      </span>
      <span className={styles.name} style={{ left: 120, top: 516 }}>
        TOM.B
      </span>
      <span className={`${styles.name} ${styles.attention}`} style={{ left: 140, top: 600 }}>
        <svg width="10" height="9" viewBox="0 0 12 11" aria-hidden="true">
          <path
            d="M6 1 L11 10 L1 10 Z"
            fill="none"
            stroke="currentColor"
            strokeWidth="1.6"
            strokeLinejoin="round"
          />
        </svg>
        INES · 1/2
      </span>
      <span
        className={`${styles.name} ${rescued ? styles.attention : styles.danger}`}
        style={{ left: 236, top: 536 }}
      >
        <svg width="10" height="9" viewBox="0 0 12 11" aria-hidden="true">
          {!rescued && <path d="M6 0 L12 11 L0 11 Z" fill="currentColor" />}
          <path
            d="M6 1 L11 10 L1 10 Z"
            fill="none"
            stroke="currentColor"
            strokeWidth="1.6"
            strokeLinejoin="round"
          />
        </svg>
        {rescued ? 'YUKI · 1/2' : 'YUKI · UN FIL'}
      </span>

      <section className={styles.panel}>
        <span className={styles.status}>TENU · 2 MAINS SUR 2</span>
        <h1 className={styles.title}>Tu tiens encore.</h1>
        <p className={styles.subline}>
          {rescued
            ? 'Yuki a attrapé ta corde. Il lui en faut encore une avant minuit.'
            : 'Il te reste une main. Yuki ne tient plus qu’à un fil.'}
        </p>
        <button
          type="button"
          className={rescued ? styles.done : styles.primary}
          onClick={rescue}
          disabled={rescued}
        >
          {rescued ? 'Main tendue à yuki' : 'Tendre ta dernière main à yuki'}
        </button>
        <button type="button" className={styles.secondary}>
          Au bord du vide · 37
        </button>
      </section>
    </div>
  );
}
