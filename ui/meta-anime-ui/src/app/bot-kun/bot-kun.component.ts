import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  ElementRef,
  inject,
  input,
  OnDestroy,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslateService, TranslatePipe } from '@ngx-translate/core';
import { marked } from 'marked';
import DOMPurify from 'dompurify';
import { BotKunService, BotKunHistoryMessage } from '../services/bot-kun.service';
import { ProviderStateService } from '../services/provider-state.service';
import { BotKunAnimeCardComponent } from './bot-kun-anime-card/bot-kun-anime-card.component';
import { FeelingLuckyCardComponent } from './feeling-lucky-card/feeling-lucky-card.component';
import { Subscription } from 'rxjs';

// Register marked extensions once: line breaks, no special link renderer needed
// (anime titles are handled via pre-processing in renderMarkdown, not via marked links)
marked.use({ breaks: true });

export type BotKunMessage = {
  role: 'user' | 'assistant';
  content: string;
  /** The portion of content that has been "typed out" and is currently visible */
  typedContent: string;
  /** True while the API SSE stream is open */
  streaming?: boolean;
  /** True while the typewriter engine still has chars to display */
  typing?: boolean;
  /** Snapshot of permittedMalIds captured when this message was finalised (undefined = all allowed) */
  permittedIds?: ReadonlySet<string>;
};

/** A parsed segment of a bot message — either plain HTML, an anime mini-card, or a Feeling Lucky card */
export type MessageSegment =
  | { type: 'text'; html: string }
  | { type: 'card'; malId: string }
  | { type: 'lucky'; malId: string };

@Component({
  selector: 'app-bot-kun',
  standalone: true,
  imports: [BotKunAnimeCardComponent, FeelingLuckyCardComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './bot-kun.component.html',
  styleUrl: './bot-kun.component.scss',
})
export class BotKunComponent implements OnDestroy {
  protected readonly botKunService = inject(BotKunService);
  private readonly providerState = inject(ProviderStateService);
  private readonly translate = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);

  private luckyCardResume: (() => void) | null = null;

  readonly currentLang = signal(this.translate.currentLang ?? 'en');

  /** Emits the MAL ID of an anime the user clicked */
  readonly animeSelected = output<string>();

  /** Set to true by AppComponent when the AnimeDetailPanel is open (PC side-by-side layout) */
  readonly detailPanelOpen = input<boolean>(false);

  readonly isOpen = signal(false);
  readonly isClosing = signal(false);
  readonly messages = signal<BotKunMessage[]>([]);
  readonly inputText = signal('');
  readonly isStreaming = signal(false);
  /** True when Bot-kun has a new response and the drawer is currently closed */
  readonly hasUnread = signal(false);

  readonly hasMessages = computed(() => this.messages().length > 0);
  readonly hasTypingMessage = computed(() => this.messages().some(m => m.typing));
  private readonly _msgCount = computed(() => this.messages().length);
  private typewriterVersion = 0;

  private readonly messagesEnd = viewChild<ElementRef<HTMLElement>>('messagesEnd');
  private readonly chatInput = viewChild<ElementRef<HTMLTextAreaElement>>('chatInput');
  private streamSub: Subscription | null = null;
  private greetingSub: Subscription | null = null;

  // Typewriter timer handle — tracked so we can cancel on destroy / new message
  private _typewriterHandle: ReturnType<typeof setTimeout> | null = null;
  // Tracks how long we've been waiting for an incomplete tag to close
  private _incompleteTagWaitMs = 0;
  // Debounce handle for scroll-to-bottom effect
  private _scrollDebounceHandle: ReturnType<typeof setTimeout> | null = null;
  // Cache for parseSegments results (non-streaming only)
  private readonly _segmentCache = new Map<string, MessageSegment[]>();

  // Track previous botkunEnabled state for open/close effect
  private _prevBotkunEnabled: boolean | null = null;

  constructor() {
    // Debounce scroll so it doesn't fire on every streamed character
    effect(() => {
      const count = this._msgCount();
      if (count > 0) {
        clearTimeout(this._scrollDebounceHandle ?? undefined);
        this._scrollDebounceHandle = setTimeout(() => this.scrollToBottom('smooth'), 100);
      }
    });

    // Open/close drawer automatically when bot-kun is enabled or disabled
    effect(() => {
      const enabled = this.providerState.botkunEnabled();
      if (this._prevBotkunEnabled !== null) {
        if (enabled && !this._prevBotkunEnabled && !this.isOpen()) {
          this.open();
        } else if (!enabled && this._prevBotkunEnabled && this.isOpen()) {
          this.close();
        }
      }
      this._prevBotkunEnabled = enabled;
    });

    // Auto-focus the textarea once the bot finishes responding
    effect(() => {
      const idle = !this.isStreaming() && !this.hasTypingMessage();
      if (idle && this.isOpen() && this.hasMessages()) {
        setTimeout(() => this.chatInput()?.nativeElement?.focus(), 50);
      }
    });

    this.translate.onLangChange
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(e => this.currentLang.set(e.lang));
  }

  ngOnDestroy(): void {
    this.streamSub?.unsubscribe();
    this.greetingSub?.unsubscribe();
    this.botKunService.cancel();
    this.botKunService.cancelGreeting();
    // Cancel any in-flight typewriter tick
    clearTimeout(this._typewriterHandle ?? undefined);
    this._typewriterHandle = null;
    // Cancel any pending scroll debounce
    clearTimeout(this._scrollDebounceHandle ?? undefined);
  }

  open(): void {
    this.isOpen.set(true);
    this.hasUnread.set(false);

    if (!this.hasMessages()) {
      // Stream an AI-generated greeting instead of the static string
      this.streamGreeting();
    } else {
      // Restore scroll to last message after DOM renders
      setTimeout(() => this.scrollToBottom(), 50);
    }
  }

  close(): void {
    if (this.isClosing()) return;
    this.isClosing.set(true);
    setTimeout(() => {
      this.isOpen.set(false);
      this.isClosing.set(false);
      // NOTE: Do NOT cancel the stream here — the conversation continues in the background
      // so the response is ready when the user reopens the drawer.
    }, 220);
  }

  handleInput(event: Event): void {
    this.inputText.set((event.target as HTMLTextAreaElement).value);
  }

  onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.send();
    }
  }

  send(): void {
    const text = this.inputText().trim();
    if (!text || this.isStreaming() || this.hasTypingMessage()) return;

    this.inputText.set('');
    this.appendMessage({ role: 'user', content: text, typedContent: text });

    const history: BotKunHistoryMessage[] = this.messages()
      .filter(m => !m.streaming)
      .map(m => ({ role: m.role, content: m.content }));

    // Add the streaming placeholder
    const botMsg: BotKunMessage = { role: 'assistant', content: '', typedContent: '', streaming: true, typing: true };
    this.appendMessage(botMsg);
    this.isStreaming.set(true);

    const msgIndex = this.messages().length - 1;
    const version = ++this.typewriterVersion;
    this.startTypewriter(msgIndex, version);

    this.streamSub?.unsubscribe();
    this.streamSub = this.botKunService.sendMessage(text, history.slice(0, -1), this.currentLang()).subscribe({
      next: chunk => {
        this.messages.update(msgs => {
          const updated = [...msgs];
          const last = updated[updated.length - 1];
          if (last?.role === 'assistant') {
            updated[updated.length - 1] = { ...last, content: last.content + chunk };
          }
          return updated;
        });
      },
      complete: () => {
        this.messages.update(msgs => {
          const updated = [...msgs];
          const last = updated[updated.length - 1];
          if (last?.role === 'assistant') {
            updated[updated.length - 1] = { ...last, streaming: false };
          }
          return updated;
        });
        this.isStreaming.set(false);
        // Notify the user if the drawer was closed while waiting for the response
        if (!this.isOpen()) {
          this.hasUnread.set(true);
        }
      },
      error: () => {
        this.messages.update(msgs => {
          const updated = [...msgs];
          const last = updated[updated.length - 1];
          if (last?.role === 'assistant' && last.streaming) {
            updated[updated.length - 1] = {
              ...last,
              content: last.content || this.translate.instant('botKun.errorFallback'),
              streaming: false,
            };
          }
          return updated;
        });
        this.isStreaming.set(false);
      },
    });
  }

  /**
   * Parse a finished bot message into display segments.
   * While still streaming, returns a single text segment so cards don't flash/reload on each chunk.
   * Deduplicates by malId — if the AI emits the same [anime:ID] tag more than once,
   * only the first occurrence is rendered as a card.
   * Tag format: [anime:ID] or [lucky:ID] — ID only, no title in the tag.
   * S6: non-streaming results are cached by content string to avoid redundant parsing.
   */
  parseSegments(content: string, streaming: boolean, permittedIds: ReadonlySet<string> = this.botKunService.permittedMalIds()): MessageSegment[] {
    if (streaming) {
      return [{ type: 'text', html: this.toSafeHtml(content) }];
    }

    // Return cached result if available; key includes permittedIds when non-empty
    // so finalized messages with different ID snapshots don't collide.
    const cacheKey = permittedIds.size > 0
      ? `${content}\x00${[...permittedIds].sort().join(',')}`
      : content;
    const cached = this._segmentCache.get(cacheKey);
    if (cached) return cached;

    // Per-call local set — deduplicates within a single message without persisting
    // across cache-miss re-parses (which happen each time the typewriter adds a char).
    const seenMalIds = new Set<string>();
    const segments: MessageSegment[] = [];
    let lastIndex = 0;
    let prevWasCard = false;

    // ID-only tag format: [anime:digits] or [lucky:digits]
    for (const match of content.matchAll(/\[(anime|lucky):(\d+)\]/g)) {
      const matchIndex = match.index ?? 0;

      if (matchIndex > lastIndex) {
        let textChunk = content.slice(lastIndex, matchIndex);
        if (prevWasCard) textChunk = this.stripLeadingTitleLine(textChunk);
        textChunk = this.stripTrailingTitleLine(textChunk);
        segments.push({ type: 'text', html: this.toSafeHtml(textChunk) });
        prevWasCard = false;
      }

      const tagType = match[1].trim() as 'anime' | 'lucky';
      const malId = match[2].trim();

      // R17b: reject [anime:ID] tags whose ID was not in the backend's PERMITTED IDs list.
      // permittedIds is empty for RAG/greeting paths (all IDs allowed) and for lucky picks.
      if (tagType === 'anime' && permittedIds.size > 0 && !permittedIds.has(malId)) {
        // Replace hallucinated tag with its ID as plain text (no title to fall back on)
        segments.push({ type: 'text', html: this.toSafeHtml(`[anime:${malId}]`) });
        prevWasCard = false;
        lastIndex = matchIndex + match[0].length;
        continue;
      }

      // Per-call dedup prevents the same ID appearing twice in one message.
      if (!seenMalIds.has(malId)) {
        seenMalIds.add(malId);
        segments.push({ type: tagType === 'lucky' ? 'lucky' : 'card', malId });
        prevWasCard = true;
      }

      lastIndex = matchIndex + match[0].length;
    }

    if (lastIndex < content.length) {
      let tail = content.slice(lastIndex);
      if (prevWasCard) tail = this.stripLeadingTitleLine(tail);
      segments.push({ type: 'text', html: this.toSafeHtml(tail) });
    }

    const result: MessageSegment[] = segments.length > 0 ? segments : [{ type: 'text', html: this.toSafeHtml(content) }];
    this._segmentCache.set(cacheKey, result);
    return result;
  }

  /** Stable track key for `@for` over MessageSegment arrays — cards track by malId, text by position. */
  segmentTrackKey(index: number, seg: MessageSegment): string {
    if (seg.type === 'card') return `card-${seg.malId}`;
    if (seg.type === 'lucky') return `lucky-${seg.malId}`;
    return `text-${index}`;
  }

  clearHistory(): void {
    this.typewriterVersion++; // cancel any running typewriter
    this.messages.set([]);
    this.botKunService.cancel();
    this.botKunService.cancelGreeting();
    this.isStreaming.set(false);
    this.hasUnread.set(false);
    this._segmentCache.clear();
  }

  onLuckyCardReady(): void {
    this.luckyCardResume?.();
    this.luckyCardResume = null;
  }

  private streamGreeting(): void {
    const botMsg: BotKunMessage = { role: 'assistant', content: '', typedContent: '', streaming: true, typing: true };
    this.appendMessage(botMsg);

    const msgIndex = this.messages().length - 1;
    const version = ++this.typewriterVersion;
    this.startTypewriter(msgIndex, version);

    this.greetingSub?.unsubscribe();
    this.greetingSub = this.botKunService.streamGreeting().subscribe({
      next: chunk => {
        this.messages.update(msgs => {
          const updated = [...msgs];
          const last = updated[updated.length - 1];
          if (last?.role === 'assistant' && last.streaming) {
            updated[updated.length - 1] = { ...last, content: last.content + chunk };
          }
          return updated;
        });
      },
      complete: () => {
        this.messages.update(msgs => {
          const updated = [...msgs];
          const last = updated[updated.length - 1];
          if (last?.role === 'assistant' && last.streaming) {
            updated[updated.length - 1] = { ...last, streaming: false };
          }
          return updated;
        });
      },
      error: () => {
        this.messages.update(msgs => {
          const updated = [...msgs];
          const last = updated[updated.length - 1];
          if (last?.role === 'assistant' && last.streaming) {
            updated[updated.length - 1] = {
              ...last,
              content: last.content || this.translate.instant('botKun.connectionError'),
              streaming: false,
            };
          }
          return updated;
        });
      },
    });
  }

  private appendMessage(msg: BotKunMessage): void {
    this.messages.update(msgs => [...msgs, msg]);
  }

  /**
   * Typewriter engine — advances `typedContent` one character (or one complete anime tag) at a time.
   * Self-schedules via setTimeout; stops when the `version` is superseded (clearHistory / new send).
   * S3: all setTimeout handles are tracked in _typewriterHandle so they can be cancelled on destroy.
   * S5: incomplete-tag wait has a 5 s safety timeout so a malformed tag can't block the typewriter.
   */
  private startTypewriter(msgIndex: number, version: number): void {
    const COMPLETE_TAG_RE = /^\[(anime|lucky):\d+\]/;
    const MIN_DELAY = 5;
    const VAR_DELAY = 5;

    clearTimeout(this._typewriterHandle ?? undefined);
    this._typewriterHandle = null;
    this._incompleteTagWaitMs = 0;

    const tick = () => {
      if (this.typewriterVersion !== version) return; // superseded — stop

      const msgs = this.messages();
      const msg = msgs[msgIndex];
      if (!msg || msg.role !== 'assistant') return;

      const buffer = msg.content;
      const displayed = msg.typedContent;

      if (displayed.length >= buffer.length) {
        if (!msg.streaming) {
          // Stream closed and every char is typed — finalise with a permittedIds snapshot
          this.messages.update(m => m.map((x, i) =>
            i === msgIndex ? { ...x, typing: false, typedContent: x.content, permittedIds: this.botKunService.permittedMalIds() } : x
          ));
          return;
        }
        // Caught up to buffer while stream is still open — poll for more chunks
        this._typewriterHandle = setTimeout(tick, 16);
        return;
      }

      const remaining = buffer.slice(displayed.length);
      let step: number;
      let nextDelay: number;

      const tagMatch = COMPLETE_TAG_RE.exec(remaining);
      if (tagMatch) {
        // Fully-formed media tag — pop the whole tag in one frame, no delay
        step = tagMatch[0].length;
        nextDelay = 0;
      } else if ((remaining.startsWith('[anime:') || remaining.startsWith('[lucky:')) && msg.streaming) {
        // Incomplete tag while stream is open — wait up to 5 s before treating as plain text
        this._incompleteTagWaitMs += 30;
        if (this._incompleteTagWaitMs > 5000) {
          // Tag never closed — treat as plain text to unblock the typewriter
          this._incompleteTagWaitMs = 0;
          step = 1;
          nextDelay = MIN_DELAY;
        } else {
          this._typewriterHandle = setTimeout(tick, 30);
          return;
        }
      } else {
        step = 1;
        nextDelay = MIN_DELAY + Math.floor(Math.random() * VAR_DELAY);
      }

      this.messages.update(m => m.map((x, i) =>
        i === msgIndex ? { ...x, typedContent: buffer.slice(0, displayed.length + step) } : x
      ));

      this.scrollToBottom('instant');

      // For lucky tags, pause typewriter until the card emits cardReady
      if (tagMatch?.[0].startsWith('[lucky:')) {
        this.luckyCardResume = () => { this.luckyCardResume = null; tick(); };
        return;
      }

      this._typewriterHandle = setTimeout(tick, nextDelay);
    };

    this._typewriterHandle = setTimeout(tick, 10);
  }

  private scrollToBottom(behavior: ScrollBehavior = 'smooth'): void {
    this.messagesEnd()?.nativeElement?.scrollIntoView({ behavior });
  }

  /** Parse markdown → sanitize HTML (no anime card tags — those are handled by parseSegments) */
  /**
   * Strips a trailing standalone heading (`### Title`) or bold-only line (`**Title**`)
   * from a markdown text chunk that immediately precedes a card segment.
   * The card itself renders the title, so this text label is redundant.
   */
  private stripTrailingTitleLine(text: string): string {
    return text
      .replace(/(?:\n|^)[ \t]*(#{1,6} [^\n]+|\*\*[^\n*]+\*\*)[ \t]*\n*$/, '')
      .trimEnd();
  }

  /**
   * Strips a leading standalone heading (`### Title`) or bold-only line (`**Title**`)
   * from a markdown text chunk that immediately follows a card segment.
   * The card already shows the title — the trailing label is redundant.
   */
  private stripLeadingTitleLine(text: string): string {
    return text
      .replace(/^[ \t]*(#{1,6} [^\n]+|\*\*[^\n*]+\*\*)[ \t]*(\n|$)/, '')
      .trimStart();
  }

  private readonly _renderer = (() => {
    const r = new marked.Renderer();
    // Safety net: headings inside the chat bubble must never get browser-default h1–h6 sizes.
    // Render them as compact labelled spans instead.
    r.heading = ({ text }: { text: string }) =>
      `<span class="bot-kun__label">${text}</span> `;
    return r;
  })();

  private toSafeHtml(text: string): string {
    const raw = marked.parse(text, { async: false, renderer: this._renderer }) as string;
    return DOMPurify.sanitize(raw);
  }
}

