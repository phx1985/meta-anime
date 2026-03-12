import {
  ChangeDetectionStrategy,
  Component,
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
import { marked } from 'marked';
import DOMPurify from 'dompurify';
import { BotSanService, BotSanHistoryMessage } from '../services/bot-san.service';
import { BotSanAnimeCardComponent } from './bot-san-anime-card/bot-san-anime-card.component';
import { Subscription } from 'rxjs';

// Register marked extensions once: line breaks, no special link renderer needed
// (anime titles are handled via pre-processing in renderMarkdown, not via marked links)
marked.use({ breaks: true });

export type BotSanMessage = {
  role: 'user' | 'assistant';
  content: string;
  /** The portion of content that has been "typed out" and is currently visible */
  typedContent: string;
  /** True while the API SSE stream is open */
  streaming?: boolean;
  /** True while the typewriter engine still has chars to display */
  typing?: boolean;
};

/** A parsed segment of a bot message — either plain HTML or an anime mini-card */
export type MessageSegment =
  | { type: 'text'; html: string }
  | { type: 'card'; malId: string; title: string };

@Component({
  selector: 'app-bot-san',
  standalone: true,
  imports: [BotSanAnimeCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './bot-san.component.html',
  styleUrl: './bot-san.component.scss',
})
export class BotSanComponent implements OnDestroy {
  private readonly botSanService = inject(BotSanService);

  /** Emits the MAL ID of an anime the user clicked */
  readonly animeSelected = output<string>();

  /** Set to true by AppComponent when the AnimeDetailPanel is open (PC side-by-side layout) */
  readonly detailPanelOpen = input<boolean>(false);

  readonly isOpen = signal(false);
  readonly isClosing = signal(false);
  readonly messages = signal<BotSanMessage[]>([]);
  readonly inputText = signal('');
  readonly isStreaming = signal(false);
  /** True when Bot-san has a new response and the drawer is currently closed */
  readonly hasUnread = signal(false);

  readonly hasMessages = computed(() => this.messages().length > 0);
  readonly hasTypingMessage = computed(() => this.messages().some(m => m.typing));
  private readonly _msgCount = computed(() => this.messages().length);
  private typewriterVersion = 0;

  private readonly messagesEnd = viewChild<ElementRef<HTMLElement>>('messagesEnd');
  private streamSub: Subscription | null = null;
  private greetingSub: Subscription | null = null;

  constructor() {
    // Smooth-scroll only when a new message is added (length changes), not on every typed char
    effect(() => {
      const count = this._msgCount();
      if (count > 0) {
        setTimeout(() => this.scrollToBottom('smooth'), 30);
      }
    });
  }

  ngOnDestroy(): void {
    this.streamSub?.unsubscribe();
    this.greetingSub?.unsubscribe();
    this.botSanService.cancel();
    this.botSanService.cancelGreeting();
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

    const history: BotSanHistoryMessage[] = this.messages()
      .filter(m => !m.streaming)
      .map(m => ({ role: m.role, content: m.content }));

    // Add the streaming placeholder
    const botMsg: BotSanMessage = { role: 'assistant', content: '', typedContent: '', streaming: true, typing: true };
    this.appendMessage(botMsg);
    this.isStreaming.set(true);

    const msgIndex = this.messages().length - 1;
    const version = ++this.typewriterVersion;
    this.startTypewriter(msgIndex, version);

    this.streamSub?.unsubscribe();
    this.streamSub = this.botSanService.sendMessage(text, history.slice(0, -1)).subscribe({
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
              content: last.content || 'Something went wrong connecting to Bot-san. Please try again!',
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
   * Deduplicates by malId — if the AI emits the same [anime:ID:Title] tag more than once,
   * only the first occurrence is rendered as a card.
   */
  parseSegments(content: string, streaming: boolean): MessageSegment[] {
    if (streaming) {
      return [{ type: 'text', html: this.toSafeHtml(content) }];
    }

    const segments: MessageSegment[] = [];
    const seenMalIds = new Set<string>();
    let lastIndex = 0;

    for (const match of content.matchAll(/\[anime:([^:\]]+):([^\]]+)\]/g)) {
      const matchIndex = match.index ?? 0;

      if (matchIndex > lastIndex) {
        segments.push({ type: 'text', html: this.toSafeHtml(content.slice(lastIndex, matchIndex)) });
      }

      const malId = match[1].trim();
      if (!seenMalIds.has(malId)) {
        seenMalIds.add(malId);
        segments.push({ type: 'card', malId, title: match[2].trim() });
      }

      lastIndex = matchIndex + match[0].length;
    }

    if (lastIndex < content.length) {
      segments.push({ type: 'text', html: this.toSafeHtml(content.slice(lastIndex)) });
    }

    return segments.length > 0 ? segments : [{ type: 'text', html: this.toSafeHtml(content) }];
  }

  /** Stable track key for `@for` over MessageSegment arrays — cards track by malId, text by position. */
  segmentTrackKey(index: number, seg: MessageSegment): string {
    return seg.type === 'card' ? `card-${seg.malId}` : `text-${index}`;
  }

  clearHistory(): void {
    this.typewriterVersion++; // cancel any running typewriter
    this.messages.set([]);
    this.botSanService.cancel();
    this.botSanService.cancelGreeting();
    this.isStreaming.set(false);
    this.hasUnread.set(false);
  }

  private streamGreeting(): void {
    const botMsg: BotSanMessage = { role: 'assistant', content: '', typedContent: '', streaming: true, typing: true };
    this.appendMessage(botMsg);

    const msgIndex = this.messages().length - 1;
    const version = ++this.typewriterVersion;
    this.startTypewriter(msgIndex, version);

    this.greetingSub?.unsubscribe();
    this.greetingSub = this.botSanService.streamGreeting().subscribe({
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
              content: last.content || "Yosh! Bot-san is here! 🎌 Ask me anything about anime — Dattebayo!",
              streaming: false,
            };
          }
          return updated;
        });
      },
    });
  }

  private appendMessage(msg: BotSanMessage): void {
    this.messages.update(msgs => [...msgs, msg]);
  }

  /**
   * Typewriter engine — advances `typedContent` one character (or one complete anime tag) at a time.
   * Self-schedules via setTimeout; stops when the `version` is superseded (clearHistory / new send).
   */
  private startTypewriter(msgIndex: number, version: number): void {
    const COMPLETE_TAG_RE = /^\[anime:[^:\]]+:[^\]]+\]/;
    const MIN_DELAY = 5;
    const VAR_DELAY = 5;

    const tick = () => {
      if (this.typewriterVersion !== version) return; // superseded — stop

      const msgs = this.messages();
      const msg = msgs[msgIndex];
      if (!msg || msg.role !== 'assistant') return;

      const buffer = msg.content;
      const displayed = msg.typedContent;

      if (displayed.length >= buffer.length) {
        if (!msg.streaming) {
          // Stream closed and every char is typed — finalise
          this.messages.update(m => m.map((x, i) =>
            i === msgIndex ? { ...x, typing: false, typedContent: x.content } : x
          ));
          return;
        }
        // Caught up to buffer while stream is still open — poll for more chunks
        setTimeout(tick, 16);
        return;
      }

      const remaining = buffer.slice(displayed.length);
      let step: number;
      let nextDelay: number;

      const tagMatch = COMPLETE_TAG_RE.exec(remaining);
      if (tagMatch) {
        // Fully-formed anime tag — pop the whole tag in one frame, no delay
        step = tagMatch[0].length;
        nextDelay = 0;
      } else if (remaining.startsWith('[anime:') && msg.streaming) {
        // Incomplete tag while stream is still open — wait for the rest of the tag
        setTimeout(tick, 30);
        return;
      } else {
        step = 1;
        nextDelay = MIN_DELAY + Math.floor(Math.random() * VAR_DELAY);
      }

      this.messages.update(m => m.map((x, i) =>
        i === msgIndex ? { ...x, typedContent: buffer.slice(0, displayed.length + step) } : x
      ));

      this.scrollToBottom('instant');
      setTimeout(tick, nextDelay);
    };

    setTimeout(tick, 10);
  }

  private scrollToBottom(behavior: ScrollBehavior = 'smooth'): void {
    this.messagesEnd()?.nativeElement?.scrollIntoView({ behavior });
  }

  /** Parse markdown → sanitize HTML (no anime card tags — those are handled by parseSegments) */
  private toSafeHtml(text: string): string {
    const raw = marked.parse(text, { async: false }) as string;
    return DOMPurify.sanitize(raw);
  }
}

