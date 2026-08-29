import { useEffect, useEffectEvent } from 'react';
import type { ClubLiveKind } from './App';

/** Подписка на SignalR-пинги клуба (см. notifyClubLive в App.tsx). */
export function useClubLive(kinds: ClubLiveKind | ClubLiveKind[], onUpdate: () => void) {
  const wanted = Array.isArray(kinds) ? kinds : [kinds];
  const key = wanted.slice().sort().join(',');

  const onLive = useEffectEvent(() => {
    onUpdate();
  });

  useEffect(() => {
    const set = new Set(wanted);
    const handler = (e: Event) => {
      const detail = (e as CustomEvent<ClubLiveKind>).detail;
      if (set.has(detail)) onLive();
    };
    window.addEventListener('club_live_update', handler);
    return () => window.removeEventListener('club_live_update', handler);
    // key покрывает смену списка kinds
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);
}
