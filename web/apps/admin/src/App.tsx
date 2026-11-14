import { useEffect } from 'react';
import { Router } from 'wouter';
import { Enroll } from './screens/Enroll.tsx';
import { Loading, LoadError, NoAccess, SignedOut } from './screens/Gate.tsx';
import { Login } from './screens/Login.tsx';
import { ReauthDialog } from './screens/ReauthDialog.tsx';
import { Shell } from './screens/Shell.tsx';
import { useAdmin } from './store.ts';

/**
 * The admin, step by step (ADR 0019): a player session, a team role, an authenticator
 * set up, then a code to open the admin session. The server enforces each step.
 */
export function App() {
  const gate = useAdmin((s) => s.gate);
  const load = useAdmin((s) => s.load);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <Router base="/admin">
      {gate.kind === 'loading' ? <Loading /> : null}
      {gate.kind === 'signed-out' ? <SignedOut /> : null}
      {gate.kind === 'no-access' ? <NoAccess /> : null}
      {gate.kind === 'error' ? <LoadError code={gate.code} /> : null}
      {gate.kind === 'enroll' ? <Enroll /> : null}
      {gate.kind === 'login' ? <Login /> : null}
      {gate.kind === 'ready' ? <Shell session={gate.session} /> : null}
      <ReauthDialog />
    </Router>
  );
}
