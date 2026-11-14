import { useEffect, useState } from 'react';
import { serverNow } from '../core/time.ts';

/** The server's time (simulated in development), refreshed every second. */
export function useServerNow(): number {
  const [now, setNow] = useState(serverNow);
  useEffect(() => {
    const timer = setInterval(() => {
      setNow(serverNow());
    }, 1000);
    return () => {
      clearInterval(timer);
    };
  }, []);
  return now;
}
