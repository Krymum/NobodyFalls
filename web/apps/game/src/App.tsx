import { lazy, Suspense, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Redirect, Route, Switch, useLocation, useParams } from 'wouter';
import { rememberInvite } from './core/invite.ts';
import { game, useGame } from './core/store.ts';
import { DevPanel } from './dev/DevPanel.tsx';
import { changeLocale, detectLocale, isLocale } from './i18n/index.ts';
import { Edge } from './screens/Edge.tsx';
import { Friends } from './screens/Friends.tsx';
import { Onboarding } from './screens/Onboarding.tsx';
import { Profile } from './screens/Profile.tsx';
import { Tonight } from './screens/tonight/Tonight.tsx';
import { World } from './screens/World.tsx';
import { SceneLayer } from './screens/tonight/SceneLayer.tsx';
import { Navigation } from './ui/Chrome.tsx';
import ui from './ui/ui.module.css';
import { useLayout } from './ui/useLayout.ts';

// The reference scene demo is a development page: kept out of the main bundle.
const SceneDemo = lazy(() => import('./dev/SceneDemo.tsx').then((m) => ({ default: m.SceneDemo })));
// What a sanctioned player sees first (GAME_DESIGN §9): rare, so its own chunk.
const NoticesScreen = lazy(() =>
  import('./notices/NoticesScreen.tsx').then((m) => ({ default: m.NoticesScreen })),
);
// "Ideas and contact" is opened from the profile: its own chunk, texts included.
const FeedbackScreen = lazy(() =>
  import('./feedback/FeedbackScreen.tsx').then((m) => ({ default: m.FeedbackScreen })),
);

/** `/fr/...` and `/en/...` (GAME_DESIGN §11); anything else is sent to the right language. */
export function App() {
  return (
    <Switch>
      <Route path="/:lang" nest>
        <Localised />
      </Route>
      <Route>
        <Redirect to={`/${detectLocale()}/`} replace />
      </Route>
    </Switch>
  );
}

function Localised() {
  const { lang } = useParams<{ lang: string }>();
  const { i18n } = useTranslation();

  useEffect(() => {
    if (isLocale(lang) && i18n.language !== lang) {
      void changeLocale(lang);
    }
  }, [lang, i18n]);

  if (!isLocale(lang)) {
    return <Redirect to={`~/${detectLocale()}/`} replace />;
  }

  return (
    <Switch>
      <Route path="/dev/scene">
        <Suspense fallback={null}>
          <SceneDemo />
        </Suspense>
      </Route>
      <Route path="/i/:code">{(params) => <Invitation code={params.code} />}</Route>
      <Route>
        <Game />
      </Route>
    </Switch>
  );
}

/** An invitation link keeps the code for the sign-up, then shows the onboarding. */
function Invitation({ code }: { code: string }) {
  useEffect(() => {
    rememberInvite(code);
  }, [code]);
  return <Redirect to="/onboarding" replace />;
}

function Game() {
  const session = useGame((s) => s.session);
  const [failed, setFailed] = useState(false);
  const [location] = useLocation();
  const { t } = useTranslation();
  const layout = useLayout();
  const notices = useGame((s) => s.notices);

  useEffect(() => {
    game.loadSession().catch(() => {
      setFailed(true);
    });
  }, []);

  // Coming back to the app: catch up with what happened meanwhile.
  useEffect(() => {
    const onVisible = (): void => {
      if (document.visibilityState === 'visible' && useGame.getState().session?.state === 'player') {
        void game.refreshAll();
      }
    };
    document.addEventListener('visibilitychange', onVisible);
    return () => {
      document.removeEventListener('visibilitychange', onVisible);
    };
  }, []);

  if (failed) {
    return (
      <main className={ui.page}>
        <p className={ui.text}>{t('errors.network.offline')}</p>
        <button
          type="button"
          className={ui.primary}
          onClick={() => {
            window.location.reload();
          }}
        >
          {t('common.retry')}
        </button>
      </main>
    );
  }
  if (!session) {
    return <main className={ui.page} aria-busy="true" />;
  }

  const registered = session.state === 'player';
  const mustSee =
    registered &&
    notices !== null &&
    (notices.ban !== null || notices.warnings.length > 0 || notices.pseudoResetPending);
  // A banned player may only reach the contact page, to contest the ban.
  const contesting = notices?.ban != null && location === '/profile/feedback';
  if (mustSee && !contesting) {
    return (
      <div className={ui.shell} data-layout={layout}>
        <div className={ui.main}>
          <Suspense fallback={<main className={ui.page} aria-busy="true" />}>
            <NoticesScreen />
          </Suspense>
        </div>
        {session.devTools && <DevPanel />}
      </div>
    );
  }

  const onTonight = registered && location === '/';
  // The scene fills the window behind every screen from tablets on (dimmed behind lists,
  // the void alone before sign-up); on phones only "Tonight" draws it, to save battery.
  const scene = onTonight ? 'full' : layout !== 'mobile' ? 'dimmed' : null;
  return (
    <div className={ui.shell} data-layout={layout}>
      {scene && <SceneLayer mode={scene} />}
      <div className={ui.main}>
        {!registered ? (
          <Onboarding />
        ) : (
          <Switch>
            <Route path="/">
              <Tonight />
            </Route>
            <Route path="/edge">
              <Edge />
            </Route>
            <Route path="/friends">
              <Friends />
            </Route>
            <Route path="/profile">
              <Profile />
            </Route>
            <Route path="/profile/feedback">
              <Suspense fallback={<main className={ui.page} aria-busy="true" />}>
                <FeedbackScreen />
              </Suspense>
            </Route>
            <Route path="/world">
              <World />
            </Route>
            <Route path="/onboarding">
              <Onboarding afterRegistration />
            </Route>
            <Route>
              <Redirect to="/" replace />
            </Route>
          </Switch>
        )}
      </div>
      {registered && location !== '/onboarding' && !contesting && <Navigation />}
      {session.devTools && <DevPanel />}
    </div>
  );
}
