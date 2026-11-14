import { api } from '../api/client.ts';

export type PushSupport = 'supported' | 'unsupported' | 'denied';

/**
 * Web Push (VAPID). iOS only allows it once the PWA is installed on the home
 * screen, which is why the onboarding asks for the installation first.
 */
export function pushSupport(): PushSupport {
  if (!('serviceWorker' in navigator) || !('PushManager' in window) || !('Notification' in window)) {
    return 'unsupported';
  }
  return Notification.permission === 'denied' ? 'denied' : 'supported';
}

function base64UrlToBytes(value: string): Uint8Array<ArrayBuffer> {
  const padded = value
    .replace(/-/g, '+')
    .replace(/_/g, '/')
    .padEnd(Math.ceil(value.length / 4) * 4, '=');
  const binary = atob(padded);
  const bytes = new Uint8Array(new ArrayBuffer(binary.length));
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes;
}

const ENABLED_KEY = 'nf-push';

/**
 * Whether this device turned notifications on, remembered locally: reading it never
 * touches the push manager, which some engines handle slowly when nothing is
 * subscribed. The server keeps the real subscriptions.
 */
export function pushEnabledHere(): boolean {
  try {
    return localStorage.getItem(ENABLED_KEY) === '1';
  } catch {
    return false;
  }
}

function rememberPush(enabled: boolean): void {
  try {
    if (enabled) {
      localStorage.setItem(ENABLED_KEY, '1');
    } else {
      localStorage.removeItem(ENABLED_KEY);
    }
  } catch {
    // Storage refused: the button simply shows "turn on" again.
  }
}

export async function currentSubscription(): Promise<PushSubscription | null> {
  if (pushSupport() === 'unsupported') {
    return null;
  }
  const registration = await navigator.serviceWorker.ready;
  return registration.pushManager.getSubscription();
}

/** Asks for permission (must follow a tap), subscribes this device and tells the server. */
export async function enablePush(): Promise<boolean> {
  if (pushSupport() !== 'supported') {
    return false;
  }
  if ((await Notification.requestPermission()) !== 'granted') {
    return false;
  }
  const registration = await navigator.serviceWorker.ready;
  const { publicKey } = await api.vapidKey();
  const subscription =
    (await registration.pushManager.getSubscription()) ??
    (await registration.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: base64UrlToBytes(publicKey),
    }));
  const json = subscription.toJSON();
  if (!json.endpoint || !json.keys?.['p256dh'] || !json.keys['auth']) {
    return false;
  }
  await api.subscribePush({
    endpoint: json.endpoint,
    keys: { p256dh: json.keys['p256dh'], auth: json.keys['auth'] },
  });
  rememberPush(true);
  return true;
}

export async function disablePush(): Promise<void> {
  rememberPush(false);
  const subscription = await currentSubscription();
  if (subscription) {
    await api.unsubscribePush(subscription.endpoint);
    await subscription.unsubscribe();
  }
}
