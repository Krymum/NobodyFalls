import type { GameEvent } from './types.ts';

export interface RealtimeConnection {
  watchEdge(watching: boolean): void;
  stop(): void;
}

/**
 * Connects to the game hub (ADR 0008). The SignalR client is loaded after the first
 * screen is shown, so it never weighs on the initial bundle. Reconnects on its own;
 * `onReconnected` lets the stores reload what they may have missed.
 */
export async function connectRealtime(
  onEvents: (events: readonly GameEvent[]) => void,
  onReconnected: () => void,
): Promise<RealtimeConnection> {
  const signalR = await import('@microsoft/signalr');
  const connection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/game')
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  connection.on('batch', (batch: { events: GameEvent[] }) => {
    onEvents(batch.events);
  });
  connection.onreconnected(onReconnected);

  let watchingEdge = false;
  connection.onreconnected(() => {
    if (watchingEdge) {
      void connection.invoke('WatchEdge', true);
    }
  });

  await connection.start();
  return {
    watchEdge(watching) {
      watchingEdge = watching;
      if (connection.state === signalR.HubConnectionState.Connected) {
        void connection.invoke('WatchEdge', watching);
      }
    },
    stop() {
      void connection.stop();
    },
  };
}
