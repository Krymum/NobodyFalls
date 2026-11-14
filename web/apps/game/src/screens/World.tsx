import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { formatNumber } from '../i18n/index.ts';
import { game, useGame } from '../core/store.ts';
import { Header } from '../ui/Chrome.tsx';
import ui from '../ui/ui.module.css';
import styles from './screens.module.css';

/** The world (GAME_DESIGN §10.6): the streak, the falls, the living and the dead. */
export function World() {
  const { t, i18n } = useTranslation();
  const world = useGame((s) => s.world);
  const tonight = useGame((s) => s.tonight);

  useEffect(() => {
    void game.loadWorld();
  }, []);

  if (!world) {
    return <main className={ui.page} aria-busy="true" />;
  }

  const n = (value: number | null): string =>
    value === null ? t('world.none') : formatNumber(value, i18n.language);
  const figures: [string, string][] = [
    [t('world.streak', { count: world.streak }), n(world.streak)],
    [t('world.lastNight'), n(world.lastNightFalls)],
    [t('world.alive'), n(world.alive)],
    [t('world.underThreshold'), n(world.underThreshold)],
    [t('world.deathsSeason'), n(world.deathsSeason)],
    [t('world.deathsAllTime'), n(world.deathsAllTime)],
  ];

  return (
    <div>
      <Header night={world.nightNumber} midnightAt={tonight?.seasonOpen ? tonight.midnightAt : null} />
      <main className={ui.page}>
        <div className={ui.section}>
          <h1 className={ui.heading}>{t('world.title')}</h1>
          {world.seasonName && world.nightNumber !== null && (
            <p className={ui.subheading}>
              {t('world.season', { name: world.seasonName, n: world.nightNumber })}
            </p>
          )}
          {world.stormTonight && <p className={`${ui.text} ${ui.attention}`}>{t('world.storm')}</p>}
        </div>
        <dl className={styles.figures}>
          {figures.map(([label, value]) => (
            <div key={label} className={styles.figure}>
              <dt className={ui.subheading}>{label}</dt>
              <dd className={styles.number}>{value}</dd>
            </div>
          ))}
        </dl>
      </main>
    </div>
  );
}
