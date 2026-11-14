import {
  CharacterRenderer,
  CodePartSet,
  prefersReducedMotion,
  startRenderLoop,
  type Look,
  type Posture,
} from '@nobodyfalls/render';
import { useEffect, useRef } from 'react';

const characters = new CharacterRenderer(new CodePartSet());

interface Props {
  readonly look: Look;
  readonly posture: Posture;
  readonly phase?: number;
  /** Height in CSS pixels; the width follows the character's proportions. */
  readonly size: number;
  /** Swing like in the scene (previews); lists draw a still pose. */
  readonly animated?: boolean;
  readonly label?: string;
}

/** One character on its own: lists, profile, character creation. */
export function CharacterCanvas({ look, posture, phase = 0, size, animated = false, label }: Props) {
  const canvas = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    const element = canvas.current;
    if (!element) {
      return undefined;
    }
    // The character hangs from its hands at the top; 130 local units cover it, rope stubs included.
    return startRenderLoop(
      element,
      ({ ctx, t, width, height }) => {
        ctx.clearRect(0, 0, width, height);
        const scale = height / 130;
        characters.draw(ctx, { x: width / 2, y: 18 * scale, scale, posture, phase, look }, t);
      },
      { reducedMotion: !animated || prefersReducedMotion() },
    );
  }, [look, posture, phase, animated]);

  return (
    <canvas
      ref={canvas}
      width={size * 0.6}
      height={size}
      style={{ width: size * 0.6, height: size, display: 'block' }}
      role={label ? 'img' : 'presentation'}
      aria-label={label}
    />
  );
}
