/**
 * Installation as a PWA (GAME_DESIGN §11): needed for push on iOS. Chrome and Edge
 * offer an install prompt we can trigger from a button; Safari needs a hint
 * ("Share → Add to Home Screen").
 */
interface InstallPrompt extends Event {
  prompt(): Promise<void>;
  readonly userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
}

let deferred: InstallPrompt | null = null;

export function registerServiceWorker(): void {
  if ('serviceWorker' in navigator && import.meta.env.PROD) {
    void navigator.serviceWorker.register('/sw.js', { scope: '/' });
  }
  window.addEventListener('beforeinstallprompt', (event) => {
    event.preventDefault();
    deferred = event as InstallPrompt;
  });
}

export function isInstalled(): boolean {
  return (
    window.matchMedia('(display-mode: standalone)').matches ||
    (navigator as Navigator & { standalone?: boolean }).standalone === true
  );
}

export function canPromptInstall(): boolean {
  return deferred !== null;
}

export function isIos(): boolean {
  return /iPhone|iPad|iPod/.test(navigator.userAgent);
}

export async function promptInstall(): Promise<boolean> {
  if (!deferred) {
    return false;
  }
  await deferred.prompt();
  const { outcome } = await deferred.userChoice;
  deferred = null;
  return outcome === 'accepted';
}
