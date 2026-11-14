/**
 * Cloudflare Turnstile, invisible: a token is fetched for each protected action
 * (registration, giving a hand) and verified by the server (GAME_DESIGN §12).
 */

/** Cloudflare's public test site key: always passes, accepts the documented dummy token. */
export const TEST_SITE_KEY = '1x00000000000000000000AA';
const DUMMY_TOKEN = 'XXXX.DUMMY.TOKEN.XXXX';

interface TurnstileApi {
  render(
    container: HTMLElement,
    options: {
      sitekey: string;
      size: 'invisible' | 'flexible';
      execution: 'execute';
      appearance: 'interaction-only';
      callback: (token: string) => void;
      'error-callback': () => void;
    },
  ): string;
  execute(widget: string): void;
  reset(widget: string): void;
  remove(widget: string): void;
}

declare global {
  interface Window {
    turnstile?: TurnstileApi;
  }
}

let loading: Promise<TurnstileApi> | null = null;

function load(): Promise<TurnstileApi> {
  loading ??= new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';
    script.async = true;
    script.onload = () => {
      if (window.turnstile) {
        resolve(window.turnstile);
      } else {
        reject(new Error('Turnstile did not load.'));
      }
    };
    script.onerror = () => {
      loading = null;
      reject(new Error('Turnstile did not load.'));
    };
    document.head.append(script);
  });
  return loading;
}

/** A fresh, single-use token. Development uses Cloudflare's test key and skips the widget. */
export async function humanToken(siteKey: string): Promise<string> {
  if (siteKey === TEST_SITE_KEY || siteKey === '') {
    return DUMMY_TOKEN;
  }

  const turnstile = await load();
  const container = document.createElement('div');
  container.hidden = true;
  document.body.append(container);
  try {
    return await new Promise<string>((resolve, reject) => {
      const widget = turnstile.render(container, {
        sitekey: siteKey,
        size: 'invisible',
        execution: 'execute',
        appearance: 'interaction-only',
        callback: resolve,
        'error-callback': () => {
          reject(new Error('Turnstile failed.'));
        },
      });
      turnstile.execute(widget);
    });
  } finally {
    container.remove();
  }
}
