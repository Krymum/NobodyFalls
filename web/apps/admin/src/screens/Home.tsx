import { useTranslation } from 'react-i18next';
import type { AdminSession } from '../api.ts';
import styles from '../styles/admin.module.css';

export function Home({ session }: { session: AdminSession }) {
  const { t } = useTranslation();
  return (
    <section aria-labelledby="home-title">
      <h1 id="home-title" className={styles.pageTitle}>
        {t('home.title', { pseudo: session.pseudo })}
      </h1>
      <dl className={styles.facts}>
        <dt className={styles.label}>{t('home.roles')}</dt>
        <dd>{session.roles.map((r) => t(`roles.${r}`)).join(', ')}</dd>
        <dt className={styles.label}>{t('home.permissions')}</dt>
        <dd>
          <ul className={styles.tags}>
            {session.permissions.map((p) => (
              <li key={p}>
                <code>{p}</code>
              </li>
            ))}
          </ul>
        </dd>
      </dl>
      <p className={styles.muted}>{t('home.soon')}</p>
    </section>
  );
}
