import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnDestroy,
  computed,
  inject,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LogsService, ServerLogEntry } from '../../services/logs.service';

type LogLevel = 'all' | 'Information' | 'Warning' | 'Error';

/**
 * "Server logs" panel: what the API is actually doing, straight from its in-process ring buffer.
 * Lives on the Activity & Logs page (the notification action "View log →" lands here via
 * /activity?view=logs) — it is the API's own log stream, not a setting.
 */
@Component({
  selector: 'app-server-logs',
  standalone: true,
  imports: [DatePipe, FormsModule, TranslatePipe],
  templateUrl: './server-logs.component.html',
  styleUrl: './server-logs.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServerLogsComponent implements OnDestroy {
  private readonly logsService = inject(LogsService);
  private readonly destroyRef = inject(DestroyRef);

  readonly entries = signal<ServerLogEntry[]>([]);
  readonly capacity = signal(0);
  readonly loading = signal(false);
  readonly error = signal(false);
  readonly level = signal<LogLevel>('all');
  readonly search = signal('');
  readonly autoRefresh = signal(true);
  readonly expanded = signal<ReadonlySet<number>>(new Set());
  readonly copied = signal(false);

  readonly counts = computed(() => {
    const items = this.entries();
    return {
      total: items.length,
      warnings: items.filter((e) => e.level === 'Warning').length,
      errors: items.filter((e) => e.level === 'Error' || e.level === 'Critical').length,
    };
  });

  readonly levels: Array<{ key: LogLevel; labelKey: string }> = [
    { key: 'all', labelKey: 'activity.logs.level.all' },
    { key: 'Information', labelKey: 'activity.logs.level.info' },
    { key: 'Warning', labelKey: 'activity.logs.level.warn' },
    { key: 'Error', labelKey: 'activity.logs.level.error' },
  ];

  private timer?: ReturnType<typeof setInterval>;

  constructor() {
    this.load();
    this.timer = setInterval(() => {
      if (this.autoRefresh()) this.load(true);
    }, 3000);

    this.destroyRef.onDestroy(() => this.stopTimer());
  }

  ngOnDestroy(): void {
    this.stopTimer();
  }

  private stopTimer(): void {
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = undefined;
    }
  }

  setLevel(level: LogLevel): void {
    if (this.level() === level) return;
    this.level.set(level);
    this.load();
  }

  onSearch(value: string): void {
    this.search.set(value);
    this.load();
  }

  toggleAutoRefresh(): void {
    this.autoRefresh.update((v) => !v);
  }

  /** Keep the panel quiet while a refresh is in flight. */
  load(silent = false): void {
    if (!silent) this.loading.set(true);
    this.logsService
      .getLogs(300, this.level(), this.search())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => {
          this.entries.set(res.items);
          this.capacity.set(res.capacity);
          this.loading.set(false);
          this.error.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.error.set(true);
        },
      });
  }

  clear(): void {
    this.logsService
      .clearLogs()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: () => this.entries.set([]), error: () => {} });
  }

  isExpanded(index: number): boolean {
    return this.expanded().has(index);
  }

  toggleException(index: number): void {
    const next = new Set(this.expanded());
    next.has(index) ? next.delete(index) : next.add(index);
    this.expanded.set(next);
  }

  async copyAll(): Promise<void> {
    const text = this.entries()
      .map((e) => `${e.timestamp} [${e.level}] ${e.category}: ${e.message}${e.exception ? '\n' + e.exception : ''}`)
      .join('\n');
    try {
      await navigator.clipboard.writeText(text);
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    } catch {
      // Clipboard can be refused (permissions/insecure context); nothing useful to do.
    }
  }

  download(): void {
    const text = this.entries()
      .map((e) => `${e.timestamp} [${e.level}] ${e.category}: ${e.message}${e.exception ? '\n' + e.exception : ''}`)
      .join('\n');
    const url = URL.createObjectURL(new Blob([text], { type: 'text/plain' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = `meta-anime-logs-${new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-')}.log`;
    link.click();
    URL.revokeObjectURL(url);
  }

  /** Maps a .NET level onto the panel's colour tokens. */
  levelClass(level: string): string {
    switch (level) {
      case 'Warning':
        return 'log-row--warn';
      case 'Error':
      case 'Critical':
        return 'log-row--error';
      default:
        return 'log-row--info';
    }
  }
}
