import { Injectable, signal, computed } from '@angular/core';

export type NotifSeverity = 'info' | 'success' | 'warning' | 'error';

export interface AppNotification {
  id: string;
  severity: NotifSeverity;
  titleKey: string;
  messageKey: string;
  timestamp: number;
  acknowledged: boolean;
  action?: { labelKey: string; route: string[]; fragment?: string };
}

const STORAGE_KEY = 'meta-anime-notifications';
const MAX_STORED = 50;
const TOAST_TTL_MS = 4500;
/** Exit animation duration — keep in sync with toastSlideToLeft in app.scss. */
const TOAST_EXIT_MS = 350;
/** Time from dismissNotifItem() call until acknowledge() removes the item.
 *  Must cover the notifItemDismiss CSS animation (400ms). */
const NOTIF_DISMISS_MS = 420;

@Injectable({ providedIn: 'root' })
export class NotificationService {
  readonly notifications = signal<AppNotification[]>(this._load());

  readonly unacknowledged = computed(() => this.notifications().filter((n) => !n.acknowledged));

  readonly unacknowledgedCount = computed(() => this.unacknowledged().length);

  readonly badgeSeverity = computed<NotifSeverity>(() => {
    const unread = this.unacknowledged();
    if (unread.some((n) => n.severity === 'error')) return 'error';
    if (unread.some((n) => n.severity === 'warning')) return 'warning';
    if (unread.some((n) => n.severity === 'success')) return 'success';
    return 'info';
  });

  readonly activeToasts = signal<AppNotification[]>([]);
  readonly leavingToastIds = signal<ReadonlySet<string>>(new Set<string>());
  readonly leavingNotifIds = signal<ReadonlySet<string>>(new Set<string>());

  /** Returns true if a notification with this exact id already exists (prevents duplicates). */
  has(id: string): boolean {
    return this.notifications().some((n) => n.id === id);
  }

  push(notif: Omit<AppNotification, 'timestamp' | 'acknowledged'>): void {
    if (this.has(notif.id)) return;
    const full: AppNotification = { ...notif, timestamp: Date.now(), acknowledged: false };
    this.notifications.update((list) => [full, ...list].slice(0, MAX_STORED));
    this._save();
    this.activeToasts.update((t) => [...t, full]);
    setTimeout(() => this.dismissToast(full.id), TOAST_TTL_MS);
  }

  acknowledge(id: string): void {
    this.notifications.update((list) =>
      list.map((n) => (n.id === id ? { ...n, acknowledged: true } : n)),
    );
    this._save();
  }

  acknowledgeAll(): void {
    this.notifications.update((list) => list.map((n) => ({ ...n, acknowledged: true })));
    this._save();
  }

  /** Animate a notif-center item out then acknowledge it (keeps the panel open). */
  dismissNotifItem(id: string): void {
    if (this.leavingNotifIds().has(id)) return;
    this.leavingNotifIds.update((s) => new Set([...s, id]));
    setTimeout(() => {
      this.acknowledge(id);
      this.leavingNotifIds.update((s) => {
        const n = new Set(s);
        n.delete(id);
        return n;
      });
    }, NOTIF_DISMISS_MS);
  }

  /** Animate all unacknowledged notif-center items out then acknowledge them. */
  dismissAllNotifItems(): void {
    const ids = this.unacknowledged().map((n) => n.id);
    ids.forEach((id) => this.leavingNotifIds.update((s) => new Set([...s, id])));
    setTimeout(() => {
      this.acknowledgeAll();
      this.leavingNotifIds.update(() => new Set<string>());
    }, NOTIF_DISMISS_MS);
  }

  dismissToast(id: string): void {
    // Already leaving — don't restart the animation
    if (this.leavingToastIds().has(id)) return;
    this.leavingToastIds.update((s) => new Set([...s, id]));
    setTimeout(() => {
      this.activeToasts.update((t) => t.filter((n) => n.id !== id));
      this.leavingToastIds.update((s) => {
        const n = new Set(s);
        n.delete(id);
        return n;
      });
    }, TOAST_EXIT_MS);
  }

  private _load(): AppNotification[] {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      return raw ? (JSON.parse(raw) as AppNotification[]) : [];
    } catch {
      return [];
    }
  }

  private _save(): void {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(this.notifications()));
    } catch {
      /* quota exceeded — ignore */
    }
  }
}
