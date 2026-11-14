import { layoutOf } from '@nobodyfalls/layout';
import { prefersReducedMotion, startRenderLoop } from '@nobodyfalls/render';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { PlayerRef, Tonight as TonightView } from '../../api/types.ts';
import { frame, sceneInsets, type Camera, type SceneMode } from '../../core/camera.ts';
import {
  layoutNeighbourhood,
  ROWS,
  SCENE_WIDTH,
  type Neighbourhood,
  type Person,
} from '../../core/neighbourhood.ts';
import { game, useGame } from '../../core/store.ts';
import { StatusMark, toneClass } from '../../ui/Chrome.tsx';
import styles from './Tonight.module.css';
import { createTonightScene, type TonightScene } from './tonightScene.ts';

type T = ReturnType<typeof useTranslation>['t'];

/** My neighbourhood, from the store, or null before the data (or before sign-up). */
export function useNeighbourhood(): Neighbourhood | null {
  const tonight = useGame((s) => s.tonight);
  const session = useGame((s) => s.session);
  const renderKeys = useGame((s) => s.renderKeys);
  const top = useGame((s) => s.edge.entries[0] ?? null);
  const player = session?.player ?? null;
  return useMemo(() => {
    if (!tonight || !player) {
      return null;
    }
    const me: PlayerRef = {
      id: player.id,
      pseudo: player.pseudo,
      isWatcher: false,
      appearance: player.appearance,
    };
    return layoutNeighbourhood(tonight, me, renderKeys, top);
  }, [tonight, player, renderKeys, top]);
}

export function describeScene(t: T, tonight: TonightView | null, candidate: Person | null): string {
  const holders = tonight?.heldBy.map((h) => h.player.pseudo).join(', ') ?? '';
  const held = tonight?.holding.map((h) => h.player.pseudo).join(', ') ?? '';
  return [
    holders ? t('tonight.sceneHeld', { holders }) : t('tonight.sceneAlone'),
    held ? t('tonight.sceneHolding', { held }) : '',
    candidate?.player ? t('tonight.sceneCandidate', { name: candidate.player.pseudo }) : '',
  ]
    .filter(Boolean)
    .join(' ');
}

/**
 * The scene behind the whole interface (ART_DIRECTION, "Responsive"): in full on
 * "Tonight", dimmed behind list screens on tablets and desktops, the void alone before
 * sign-up. Mounted once by the shell, so moving between screens never restarts it.
 */
export function SceneLayer({ mode }: { mode: SceneMode }) {
  const { t } = useTranslation();
  const neighbourhood = useNeighbourhood();
  const tonight = useGame((s) => s.tonight);
  const registered = useGame((s) => s.session?.state === 'player');
  const lastThrow = useGame((s) => s.lastThrow);
  const stage = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState({ width: 0, height: 0 });

  // The scene needs Tonight and the top of the Edge, whichever screen is open.
  useEffect(() => {
    if (registered) {
      void game.loadTonight();
      void game.loadEdge();
    }
  }, [registered]);

  useEffect(() => {
    const element = stage.current;
    if (!element) {
      return undefined;
    }
    const observer = new ResizeObserver(([entry]) => {
      if (entry) {
        setSize({ width: entry.contentRect.width, height: entry.contentRect.height });
      }
    });
    observer.observe(element);
    return () => {
      observer.disconnect();
    };
  }, []);

  const view = sceneInsets(layoutOf(size.width), mode, size.width);
  const camera = frame(size.width, size.height, view.insets, view.focus);
  const showPeople = mode === 'full' ? neighbourhood : null;

  return (
    <div ref={stage} className={styles.stage} data-mode={mode}>
      <SceneCanvas
        neighbourhood={showPeople}
        view={view}
        label={describeScene(t, tonight, neighbourhood?.candidate ?? null)}
        lastThrowKey={lastThrow}
      />
      {showPeople && size.width > 0 && (
        <Labels neighbourhood={showPeople} camera={camera} width={size.width} />
      )}
    </div>
  );
}

/** Names floating next to each character, placed by the same camera as the drawing. */
function Labels({
  neighbourhood,
  camera,
  width,
}: {
  neighbourhood: Neighbourhood;
  camera: Camera;
  width: number;
}) {
  const { t } = useTranslation();
  const x = (cx: number) => camera.offsetX + cx * camera.scale;
  const y = (cy: number) => camera.offsetY + cy * camera.scale;
  return (
    <div
      className={styles.labels}
      aria-hidden="true"
      style={{ ['--scene-scale' as string]: String(camera.scale) }}
    >
      {neighbourhood.people.map((person) => {
        const left = person.labelSide === 'left';
        const name =
          person.role === 'me'
            ? t('common.me')
            : person.player?.isWatcher
              ? t('common.watcher')
              : (person.player?.pseudo ?? '').toUpperCase();
        const detail =
          person.role === 'candidate' && person.posture === 'danger' ? t('common.thread') : person.count;
        const tone = person.role === 'me' ? 'calm' : person.tone;
        return (
          <span
            key={person.id}
            className={`${styles.name} ${person.role === 'me' ? styles.me : ''} ${toneClass(tone)}`}
            style={{
              top: y(person.y + person.labelDy),
              ...(left
                ? { right: width - x(person.x - 26 * person.scale) }
                : { left: x(person.x + 22 * person.scale) }),
            }}
          >
            <StatusMark tone={tone} />
            {name}
            {detail && person.role !== 'holder' ? ` · ${detail}` : ''}
          </span>
        );
      })}
      {neighbourhood.hiddenHolders > 0 && (
        <span
          className={styles.name}
          style={{ top: y(ROWS.holders + 40), right: width - x(SCENE_WIDTH * 0.96) }}
        >
          {t('tonight.more', { count: neighbourhood.hiddenHolders })}
        </span>
      )}
      {neighbourhood.hiddenHeld > 0 && (
        <span className={styles.name} style={{ top: y(ROWS.held + 40), left: x(SCENE_WIDTH * 0.04) }}>
          {t('tonight.more', { count: neighbourhood.hiddenHeld })}
        </span>
      )}
    </div>
  );
}

function SceneCanvas({
  neighbourhood,
  view,
  label,
  lastThrowKey,
}: {
  neighbourhood: Neighbourhood | null;
  view: ReturnType<typeof sceneInsets>;
  label: string;
  lastThrowKey: ReturnType<typeof useGame.getState>['lastThrow'];
}) {
  const canvas = useRef<HTMLCanvasElement>(null);
  const scene = useRef<TonightScene | null>(null);
  const redraw = useRef<(() => void) | null>(null);

  useEffect(() => {
    const element = canvas.current;
    if (!element) {
      return undefined;
    }
    const created = createTonightScene();
    scene.current = created;
    let latest: Parameters<Parameters<typeof startRenderLoop>[1]>[0] | null = null;
    redraw.current = () => {
      if (latest) {
        created.draw(latest);
      }
    };
    const stop = startRenderLoop(
      element,
      (f) => {
        latest = f;
        created.draw(f);
      },
      {
        reducedMotion: prefersReducedMotion(),
        onResize: (w, h, ratio) => {
          created.resize(w, h, ratio);
        },
      },
    );
    return () => {
      stop();
      created.dispose();
      scene.current = null;
      redraw.current = null;
    };
  }, []);

  const { left, right, top, bottom } = view.insets;
  useEffect(() => {
    scene.current?.view({ left, right, top, bottom }, view.focus);
    scene.current?.show(neighbourhood, lastThrowKey, prefersReducedMotion());
    // With reduced motion the loop is still: draw the new state once.
    redraw.current?.();
  }, [neighbourhood, lastThrowKey, left, right, top, bottom, view.focus]);

  return <canvas ref={canvas} className={styles.canvas} role="img" aria-label={label} />;
}
