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
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslateService, TranslatePipe } from '@ngx-translate/core';
import { marked } from 'marked';
import DOMPurify from 'dompurify';
import { BotKunService, BotKunHistoryMessage } from '../services/bot-kun.service';
import { LibraryIntelStore } from '../services/library-intel-store.service';
import {
  BotKunActionPayload,
  buildBotKunLibraryContext,
  buildLeaderboard,
  estimateTranscodeSavings,
  parseBotKunActionTag,
} from '../models/library-intel.models';
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

/** A parsed segment of a bot message — plain HTML, an anime mini-card, a Feeling Lucky card,
 *  an [act:…] action CTA, or a `[id]:Title` inline title chip */
export type MessageSegment =
  | { type: 'text'; html: string }
  | { type: 'card'; malId: string }
  | { type: 'lucky'; malId: string }
  | { type: 'action'; action: BotKunActionPayload }
  | { type: 'titleChip'; id: string; title: string };

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
  private readonly libraryStore = inject(LibraryIntelStore);
  private readonly translate = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);

  private luckyCardResume: (() => void) | null = null;

  readonly currentLang = signal(this.translate.getCurrentLang() ?? 'en');

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

  readonly speakingIndex = signal<number>(-1);
  /** True for ~300 ms on each word boundary — drives the CSS pulse class on the visualizer */
  readonly speakingPulse = signal<boolean>(false);

  /** Keys of [act:…] CTAs already applied — persistent per-segment "✓ Applied" state */
  readonly appliedActionKeys = signal<ReadonlySet<string>>(new Set());

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
  // Waveform pulse timer handle
  private _waveformDecayHandle: ReturnType<typeof setTimeout> | null = null;
  // Proactive library-insight: armed on open() when rows exist, fired after the greeting finishes
  private _libraryInsightPending = false;
  // Poll handle waiting for the greeting typewriter to drain before appending the insight
  private _insightPollHandle: ReturnType<typeof setTimeout> | null = null;
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

    // Open drawer and send a prompt queued from outside (e.g. anime detail panel "Ask Bot-kun" button)
    effect(() => {
      const prompt = this.botKunService.pendingPrompt();
      if (prompt === null) return;
      untracked(() => {
        this.botKunService.consumePrompt();
        if (!this.isOpen()) {
          this.isOpen.set(true);
          this.hasUnread.set(false);
        }
        this.inputText.set(prompt);
        // Send after a short tick; if Bot-kun is busy, the text stays in the input box.
        setTimeout(() => {
          if (!this.isStreaming() && !this.hasTypingMessage()) {
            this.send();
          }
          // else: text remains pre-filled — user sends manually when ready
        }, 50);
      });
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
    clearTimeout(this._typewriterHandle ?? undefined);
    this._typewriterHandle = null;
    clearTimeout(this._scrollDebounceHandle ?? undefined);
    clearTimeout(this._waveformDecayHandle ?? undefined);
    clearTimeout(this._insightPollHandle ?? undefined);
    this.stopSpeech();
  }

  open(): void {
    this.isOpen.set(true);
    this.hasUnread.set(false);

    if (!this.hasMessages()) {
      // Proactive Library Buddy insight: only when the analysis table has rows
      this._libraryInsightPending = this.libraryStore.rows().length > 0;
      // Stream an AI-generated greeting instead of the static string
      this.streamGreeting();
    } else {
      // Restore scroll to last message after DOM renders
      setTimeout(() => this.scrollToBottom(), 50);
    }
  }

  close(): void {
    if (this.isClosing()) return;
    this.stopSpeech();
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
    this.dispatchMessage(text);
  }

  /** Fast-path: Feeling Lucky — skips LLM intent extraction entirely (action="lucky"). */
  surpriseMe(): void {
    if (this.isStreaming() || this.hasTypingMessage()) return;
    this.dispatchMessage(this.translate.instant('botKun.surpriseMe'), 'lucky');
  }

  /** Fast-path: next page of the previous search via the signed cursor (action="more"). */
  sendMore(): void {
    const cursor = this.botKunService.nextPageCursor();
    if (!cursor || this.isStreaming() || this.hasTypingMessage()) return;
    this.dispatchMessage(this.translate.instant('botKun.showMore'), 'more', cursor);
  }

  /** Quick-prompt chip: sends the translated chip text as a normal user message. */
  sendChip(key: string): void {
    if (this.isStreaming() || this.hasTypingMessage()) return;
    this.dispatchMessage(this.translate.instant(key));
  }

  private dispatchMessage(text: string, action?: 'lucky' | 'more', cursor?: string): void {
    this.appendMessage({ role: 'user', content: text, typedContent: text });
    this.scrollToBottom('smooth');

    const history: BotKunHistoryMessage[] = this.messages()
      .filter(m => !m.streaming)
      .map(m => ({ role: m.role, content: this.stripActionTags(m.content) }));

    // Ultra Library Buddy: prepend the library snapshot + persona note as a system entry
    const libraryContext = this.buildLibrarySystemContext();
    const apiHistory: BotKunHistoryMessage[] = history.slice(0, -1);
    if (libraryContext) {
      apiHistory.unshift({ role: 'system', content: libraryContext });
    }

    // Add the streaming placeholder
    const botMsg: BotKunMessage = { role: 'assistant', content: '', typedContent: '', streaming: true, typing: true };
    this.appendMessage(botMsg);
    this.isStreaming.set(true);

    const msgIndex = this.messages().length - 1;
    const version = ++this.typewriterVersion;
    this.startTypewriter(msgIndex, version);

    this.streamSub?.unsubscribe();
    this.streamSub = this.botKunService.sendMessage(text, apiHistory, this.currentLang(), undefined, undefined, action, cursor).subscribe({
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
   * Rewrites `content` so that card tags ([anime:ID] / [lucky:ID]) that appear mid-sentence
   * are moved to the next sentence boundary (after `.`, `!`, or `?` followed by whitespace/end).
   * This prevents visual anime cards from breaking the flow of a sentence.
   * Tags already at a boundary (after punctuation, newline, or at start) are left in place.
   * Tags with no following boundary are moved to the end of the content.
   */
  private pushCardsToSentenceBoundaries(content: string): string {
    // Split into alternating text / tag pieces while preserving the tag strings.
    const TAG_SPLIT_RE = /(\[(anime|lucky):\s*\d+\s*\])/g;
    const pieces = content.split(TAG_SPLIT_RE).filter((_, i) => i % 3 !== 2); // drop the inner capture group
    // After split with one capturing group: [text, tag, text, tag, ...]

    // First sentence-ending punctuation followed by whitespace or end-of-string
    const SENTENCE_END_RE = /[.!?](?=\s|$)/;
    // Trailing boundary check: text that ends with . ! ? \n (trimmed)
    const ENDS_WITH_BOUNDARY_RE = /[.!?\n]\s*$/;

    const out: string[] = [];
    const pending: string[] = []; // deferred card tags waiting for a sentence boundary

    for (const piece of pieces) {
      if (TAG_SPLIT_RE.test(piece)) {
        // Reset lastIndex after the sticky test
        TAG_SPLIT_RE.lastIndex = 0;
        // It's a card tag — check if preceding output ends at a sentence boundary
        const preceding = out.join('');
        if (preceding.trim() === '' || ENDS_WITH_BOUNDARY_RE.test(preceding)) {
          out.push(piece); // already at a boundary — place immediately
        } else {
          pending.push(piece); // mid-sentence — defer
        }
      } else {
        // It's a text piece
        TAG_SPLIT_RE.lastIndex = 0;
        if (pending.length === 0) {
          out.push(piece);
        } else {
          // Find the first sentence boundary in this text piece
          const m = SENTENCE_END_RE.exec(piece);
          if (m !== null) {
            // Split: text up to (and including) boundary punctuation → deferred tags → remainder
            const splitAt = m.index + 1;
            out.push(piece.slice(0, splitAt));
            out.push(...pending);
            pending.length = 0;
            out.push(piece.slice(splitAt));
          } else {
            // No boundary in this piece — keep accumulating text, stay deferred
            out.push(piece);
          }
        }
      }
    }

    // Any tags still pending go at the very end
    if (pending.length > 0) {
      out.push(...pending);
    }

    return out.join('');
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

    // Move any card tags that appear mid-sentence to the next sentence boundary for readability.
    const processedContent = this.pushCardsToSentenceBoundaries(content);

    // Per-call local set — deduplicates within a single message without persisting
    // across cache-miss re-parses (which happen each time the typewriter adds a char).
    const seenMalIds = new Set<string>();
    const segments: MessageSegment[] = [];
    let lastIndex = 0;
    let prevWasCard = false;

    // ID-only tag format: [anime:digits] or [lucky:digits]; allow optional whitespace around the ID.
    // Also matches single-line [act:{json}] action tags (group 3 = JSON payload)
    // and raw `[id]:Title` spans (group 4 = id, group 5 = title) → title chips.
    for (const match of processedContent.matchAll(/\[(anime|lucky):\s*(\d+)\s*\]|\[act:(\{[^\n]*?\})\]|\[(\d+)\]:([^\n\[]{1,80})/g)) {
      const matchIndex = match.index ?? 0;
      const actionJson = match[3];

      // [act:…] span — only consumed when it parses; malformed tags stay as literal text
      if (actionJson !== undefined) {
        const action = parseBotKunActionTag(actionJson);
        if (!action) continue; // leave the raw tag inside the next text chunk
        if (matchIndex > lastIndex) {
          let textChunk = processedContent.slice(lastIndex, matchIndex);
          if (prevWasCard) textChunk = this.stripLeadingTitleLine(textChunk);
          segments.push({ type: 'text', html: this.toSafeHtml(textChunk) });
          prevWasCard = false;
        }
        segments.push({ type: 'action', action });
        lastIndex = matchIndex + match[0].length;
        continue;
      }

      // [id]:Title span — becomes a clickable title chip; malformed variants never match
      // and stay as literal text in the surrounding chunk.
      const chipId = match[4];
      if (chipId !== undefined) {
        const title = (match[5] ?? '').trim();
        if (matchIndex > lastIndex) {
          let textChunk = processedContent.slice(lastIndex, matchIndex);
          if (prevWasCard) textChunk = this.stripLeadingTitleLine(textChunk);
          segments.push({ type: 'text', html: this.toSafeHtml(textChunk) });
          prevWasCard = false;
        }
        if (title) {
          segments.push({ type: 'titleChip', id: chipId, title });
        } else {
          // Degenerate whitespace-only title — keep the raw span as literal text
          segments.push({ type: 'text', html: this.toSafeHtml(match[0]) });
        }
        lastIndex = matchIndex + match[0].length;
        continue;
      }

      if (matchIndex > lastIndex) {
        let textChunk = processedContent.slice(lastIndex, matchIndex);
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

    if (lastIndex < processedContent.length) {
      let tail = processedContent.slice(lastIndex);
      if (prevWasCard) tail = this.stripLeadingTitleLine(tail);
      segments.push({ type: 'text', html: this.toSafeHtml(tail) });
    }

    const result: MessageSegment[] = segments.length > 0 ? segments : [{ type: 'text', html: this.toSafeHtml(processedContent) }];
    this._segmentCache.set(cacheKey, result);
    return result;
  }

  /** Stable track key for `@for` over MessageSegment arrays — cards track by malId, chips by id, text/action by position. */
  segmentTrackKey(index: number, seg: MessageSegment): string {
    if (seg.type === 'card') return `card-${seg.malId}`;
    if (seg.type === 'lucky') return `lucky-${seg.malId}`;
    if (seg.type === 'titleChip') return `chip-${index}-${seg.id}`;
    if (seg.type === 'action') return `act-${index}`;
    return `text-${index}`;
  }

  /** Type-based verb label key for an [act:…] CTA. */
  actionVerbKey(action: BotKunActionPayload): string {
    return action.type === 'applyFilter' ? 'botKun.actions.applyFilter' : 'botKun.actions.openSeries';
  }

  /** Stable per-message + per-payload key — applied state survives segment re-parses. */
  private actionKey(msgIndex: number, action: BotKunActionPayload): string {
    return `${msgIndex}:${JSON.stringify(action)}`;
  }

  /** True once this CTA pill has been pressed — persistent per-segment state. */
  isActionApplied(msgIndex: number, action: BotKunActionPayload): boolean {
    return this.appliedActionKeys().has(this.actionKey(msgIndex, action));
  }

  /** Screen-reader label for an [act:…] pill — the verb, or the applied state once pressed. */
  actionAriaLabel(msgIndex: number, action: BotKunActionPayload): string {
    const verb = this.isActionApplied(msgIndex, action)
      ? this.translate.instant('botKun.actions.applied')
      : this.translate.instant(this.actionVerbKey(action));
    return `${action.label} — ${verb}`;
  }

  /**
   * Dispatches an [act:…] payload to the LibraryIntelStore (applyFilter resets filters first)
   * and permanently marks this pill as "✓ Applied".
   */
  onActionClick(msgIndex: number, action: BotKunActionPayload): void {
    this.libraryStore.applyBotKunAction(action);
    const key = this.actionKey(msgIndex, action);
    this.appliedActionKeys.update(keys => new Set(keys).add(key));
  }

  /**
   * Click on a `[id]:Title` chip — opens the Library drawer for the series when the Sonarr ID
   * is in the loaded table, otherwise falls back to the [anime:ID] detail-panel flow.
   */
  onTitleChipClick(id: string): void {
    const sonarrId = Number(id);
    if (this.libraryStore.rows().some(r => r.sonarrId === sonarrId)) {
      this.libraryStore.openSeries(sonarrId);
    } else {
      this.animeSelected.emit(id);
    }
  }

  clearHistory(): void {
    this.stopSpeech();
    this.typewriterVersion++; // cancel any running typewriter
    this.messages.set([]);
    this.botKunService.cancel();
    this.botKunService.cancelGreeting();
    this.isStreaming.set(false);
    this.hasUnread.set(false);
    this.appliedActionKeys.set(new Set());
    this._libraryInsightPending = false;
    clearTimeout(this._insightPollHandle ?? undefined);
    this._insightPollHandle = null;
    this._segmentCache.clear();
  }

  onLuckyCardReady(): void {
    this.luckyCardResume?.();
    this.luckyCardResume = null;
  }

  toggleSpeak(index: number, content: string): void {
    if (this.speakingIndex() === index) {
      this.stopSpeech();
      return;
    }
    this.stopSpeech();
    const text = this.toPlainText(content);
    if (!text.trim() || !('speechSynthesis' in window)) return;

    const utterance = new SpeechSynthesisUtterance(text);
    utterance.lang = this.currentLang() === 'fr' ? 'fr-FR' : 'en-US';
    utterance.rate = 0.92;
    utterance.pitch = 1.05;

    // Attempt to set a voice; if voices aren't loaded yet, retry once after voiceschanged
    const trySetVoice = () => {
      const voice = this.bestVoice(utterance.lang);
      if (voice) utterance.voice = voice;
    };
    const voices = window.speechSynthesis.getVoices();
    if (voices.length === 0) {
      window.speechSynthesis.onvoiceschanged = () => {
        window.speechSynthesis.onvoiceschanged = null;
        trySetVoice();
        window.speechSynthesis.speak(utterance);
      };
      this.speakingIndex.set(index);
      return;
    }
    trySetVoice();

    utterance.onboundary = (e) => {
      if (e.name !== 'word') return;
      this.speakingPulse.set(true);
      clearTimeout(this._waveformDecayHandle ?? undefined);
      this._waveformDecayHandle = setTimeout(() => {
        this.speakingPulse.set(false);
      }, 300);
    };
    utterance.onend = () => { this.speakingIndex.set(-1); this.speakingPulse.set(false); };
    utterance.onerror = () => { this.speakingIndex.set(-1); this.speakingPulse.set(false); };
    this.speakingIndex.set(index);
    window.speechSynthesis.speak(utterance);
  }

  stopSpeech(): void {
    if ('speechSynthesis' in window) {
      window.speechSynthesis.onvoiceschanged = null;
      window.speechSynthesis.cancel();
    }
    clearTimeout(this._waveformDecayHandle ?? undefined);
    this._waveformDecayHandle = null;
    this.speakingIndex.set(-1);
    this.speakingPulse.set(false);
  }

  private bestVoice(lang: string): SpeechSynthesisVoice | null {
    if (!('speechSynthesis' in window)) return null;
    const voices = window.speechSynthesis.getVoices();
    if (!voices.length) return null;
    const base = lang.split('-')[0];

    // Priority: Azure Neural (Edge) > any "Neural" branded > Google > Microsoft > lang match
    return (
      voices.find(v => v.lang === lang   && v.name.includes('Online (Natural)')) ??
      voices.find(v => v.lang === lang   && v.name.toLowerCase().includes('neural')) ??
      voices.find(v => v.lang === lang   && v.name.includes('Google')) ??
      voices.find(v => v.lang.startsWith(base) && v.name.includes('Online (Natural)')) ??
      voices.find(v => v.lang.startsWith(base) && v.name.toLowerCase().includes('neural')) ??
      voices.find(v => v.lang.startsWith(base) && v.name.includes('Google')) ??
      voices.find(v => v.lang.startsWith(base) && v.name.includes('Microsoft')) ??
      voices.find(v => v.lang.startsWith(base)) ??
      null
    );
  }

  private toPlainText(content: string): string {
    // Strip card tags, [act:…] action tags AND raw `[id]:Title` spans — none should be read aloud
    const withoutTags = content
      .replace(/\[(anime|lucky):\d+\]/g, '')
      .replace(/\[act:\{[^\n]*?\}\]/g, '')
      .replace(/\[\d+\]:[^\n\[]{1,80}/g, '');
    const div = document.createElement('div');
    div.innerHTML = withoutTags;
    return div.textContent ?? '';
  }

  /** Removes well-formed [act:…] spans from history content before it is sent back to the API. */
  private stripActionTags(content: string): string {
    return content.replace(/\[act:\{[^\n]*?\}\]/g, '').trim();
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
        this.scheduleLibraryInsight();
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
        this.scheduleLibraryInsight();
      },
    });
  }

  private appendMessage(msg: BotKunMessage): void {
    this.messages.update(msgs => [...msgs, msg]);
  }

  // ── Ultra Library Buddy ──────────────────────────────────────────────────────

  /**
   * One-shot system prompt: compact library snapshot (rows + transcode savings +
   * active filters) followed by the Library Buddy persona / [act:…] tag grammar.
   * Returns null when the analysis table has no rows (no context to send).
   */
  private buildLibrarySystemContext(): string | null {
    const rows = this.libraryStore.rows();
    if (rows.length === 0) return null;
    const snapshot = buildBotKunLibraryContext(
      rows,
      estimateTranscodeSavings(rows),
      this.activeFilterSummary(),
    );
    return `${snapshot}\n\nLIBRARY BUDDY MODE: be punchy, data-driven, slight otaku flair (use terms like filler bloat, power level, S-tier compression). ` +
      `You may emit AT MOST ONE action tag per reply, as a single-line [act:{...}] JSON where the object is ` +
      `{"type":"applyFilter","label":"<short label>","filter":<partial filter JSON>} or ` +
      `{"type":"openSeries","label":"<short label>","sonarrId":<number>}. ` +
      `Filter vocabulary: title, videoCodec:[...], audioCodec:[...], resolution:[...], ` +
      `codecThreshold:{mode:any|strict|mixed|custom,percent:0-100,op:gte|lte}, splitCodecsOnly:bool, ` +
      `status:all|seriesEndedComplete|seasonEndedHiatus|airingWeekly|incompleteOnDisk|transcodeReady, ` +
      `bitDepth:"8"|"10", gibPerHour:"<number as string>", avgEpSize:"<number as string>", ` +
      `download:"all"|"100"|"partial", hasFiles:"all"|"yes"|"no". ` +
      `Suggested mappings: prime targets = {videoCodec:["H264"],codecThreshold:{mode:"custom",percent:50,op:"gte"},gibPerHour:"2",status:"transcodeReady"}; ` +
      `mixed/unfinished = {splitCodecsOnly:true,videoCodec:["H264"],codecThreshold:{mode:"any",percent:0,op:"gte"}}.`;
  }

  /** Short human-readable summary of the filters currently active on the analysis table. */
  private activeFilterSummary(): string {
    const f = this.libraryStore.filters();
    const parts: string[] = [];
    if (f.title) parts.push(`title~"${f.title}"`);
    if (f.videoCodec.length) parts.push(`videoCodec=${f.videoCodec.join('/')}`);
    if (f.audioCodec.length) parts.push(`audioCodec=${f.audioCodec.join('/')}`);
    if (f.resolution.length) parts.push(`resolution=${f.resolution.join('/')}`);
    if (f.codecThreshold.mode !== 'any') {
      parts.push(f.codecThreshold.mode === 'custom'
        ? `codecThreshold=${f.codecThreshold.percent}% ${f.codecThreshold.op}`
        : `codecThreshold=${f.codecThreshold.mode}`);
    }
    if (f.splitCodecsOnly) parts.push('splitCodecsOnly');
    if (f.status !== 'all') parts.push(`status=${f.status}`);
    if (f.bitDepth) parts.push(`bitDepth=${f.bitDepth}`);
    if (f.gibPerHour) parts.push(`gibPerHour>${f.gibPerHour}`);
    if (f.avgEpSize) parts.push(`avgEpSize=${f.avgEpSize}`);
    if (f.download !== 'all') parts.push(`download=${f.download}`);
    if (f.hasFiles !== 'all') parts.push(`hasFiles=${f.hasFiles}`);
    return parts.join(', ');
  }

  /**
   * Proactive greeting insight — synthetic assistant message (no LLM) built from the
   * biggest GiB/h offender plus the library-wide transcode savings, ending with a
   * prime-targets [act:…] tag so the CTA button renders.
   */
  private buildLibraryInsight(): string {
    const rows = this.libraryStore.rows();
    const [top] = buildLeaderboard(rows, 'bloatGibPerHour', 1);
    if (!top) return '';
    const savings = estimateTranscodeSavings(rows);
    const GIB = 1_073_741_824;
    const itsGb = top.reclaimableBytes / GIB;
    const totalGb = savings.reclaimableBytes / GIB;
    const tag = '[act:' + JSON.stringify({
      type: 'applyFilter',
      label: 'Prime transcode targets',
      filter: {
        videoCodec: ['H264'],
        codecThreshold: { mode: 'custom', percent: 50, op: 'gte' },
        gibPerHour: '2',
        status: 'transcodeReady',
      },
    }) + ']';
    return `Yo! ${top.title} is sitting at ${top.gibPerHour.toFixed(2)} GiB/h — transcoding its H264 saves ~${itsGb.toFixed(1)} GB ` +
      `(~${totalGb.toFixed(0)} GB across the whole library). Want me to filter your prime targets?\n${tag}`;
  }

  /**
   * Appends the synthetic library insight once the streamed greeting has fully typed out,
   * so the single typewriter instance is never hijacked mid-message.
   */
  private scheduleLibraryInsight(): void {
    if (!this._libraryInsightPending) return;
    const deadline = Date.now() + 15_000;
    const check = () => {
      this._insightPollHandle = null;
      if (!this._libraryInsightPending) return;
      const msgs = this.messages();
      const last = msgs[msgs.length - 1];
      if (msgs.length > 0 && last?.role === 'assistant' && last.typing && Date.now() < deadline) {
        this._insightPollHandle = setTimeout(check, 120);
        return;
      }
      this._libraryInsightPending = false;
      // Safety: if the greeting typewriter is still stuck (e.g. paused on a lucky card),
      // finalise every typing message so the input can't stay disabled forever.
      if (last?.typing) {
        this.messages.update(m => m.map(x => x.typing ? { ...x, typing: false, typedContent: x.content } : x));
      }
      const insight = this.buildLibraryInsight();
      if (!insight) return;
      this.appendMessage({ role: 'assistant', content: insight, typedContent: '', streaming: false, typing: true });
      const msgIndex = this.messages().length - 1;
      const version = ++this.typewriterVersion;
      this.startTypewriter(msgIndex, version);
    };
    check();
  }

  /**
   * Typewriter engine — advances `typedContent` one character (or one complete anime tag) at a time.
   * Self-schedules via setTimeout; stops when the `version` is superseded (clearHistory / new send).
   * S3: all setTimeout handles are tracked in _typewriterHandle so they can be cancelled on destroy.
   * S5: incomplete-tag wait has a 5 s safety timeout so a malformed tag can't block the typewriter.
   */
  private startTypewriter(msgIndex: number, version: number): void {
    const COMPLETE_TAG_RE = /^(?:\[(anime|lucky):\s*\d+\s*\]|\[act:\{[^\n]*?\}\])/;
    // `[id]:Title` chip token — matched atomically so chips never render with truncated titles
    const TITLE_CHIP_START_RE = /^\[\d/;
    const TITLE_CHIP_TOKEN_RE = /^\[(\d+)\]:([^\n\[]{0,80})/;
    const TITLE_CHIP_PARTIAL_RE = /^\[\d{0,12}\]?:?$/; // `[id` / `[id]` / `[id]:` still arriving
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
      } else if (TITLE_CHIP_START_RE.test(remaining)) {
        // `[id]:Title` chip token — pop it whole so the chip never renders with a truncated
        // title; wait at the buffer edge while the token/title may still be arriving.
        const chipMatch = TITLE_CHIP_TOKEN_RE.exec(remaining);
        const titleLen = chipMatch?.[2].length ?? 0;
        const tokenLen = chipMatch?.[0].length ?? 0;
        const moreFollows = chipMatch !== null && tokenLen < remaining.length; // '\n', '[' or 80-char cap ended the title
        const popToken = chipMatch !== null && titleLen > 0 && (moreFollows || !msg.streaming);
        const waitForMore = msg.streaming && !moreFollows &&
          (chipMatch !== null || TITLE_CHIP_PARTIAL_RE.test(remaining));
        if (popToken) {
          step = tokenLen;
          nextDelay = 0;
        } else if (waitForMore) {
          this._incompleteTagWaitMs += 30;
          if (this._incompleteTagWaitMs > 5000) {
            // Token never completed — treat as plain text to unblock the typewriter
            this._incompleteTagWaitMs = 0;
            step = 1;
            nextDelay = MIN_DELAY;
          } else {
            this._typewriterHandle = setTimeout(tick, 30);
            return;
          }
        } else {
          // Malformed / empty-title variant — type it out literally
          step = 1;
          nextDelay = MIN_DELAY + Math.floor(Math.random() * VAR_DELAY);
        }
      } else if ((remaining.startsWith('[anime:') || remaining.startsWith('[lucky:') || remaining.startsWith('[act:')) && msg.streaming) {
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
    // Valid [act:…] tags become CTAs (or are mid-stream) — never render them as text.
    // Malformed ones fall through and render literally.
    const withoutActions = text.replace(/\[act:(\{[^\n]*?\})\]/g, (full, json: string) =>
      parseBotKunActionTag(json) ? '' : full);
    // Keep malformed `[label]:` chip variants literal — marked would silently swallow bare
    // link-reference definitions like `[abc]: Title` sitting at the start of a line.
    const escapedDefs = withoutActions.replace(/\[([^\n\[\]]*)\]:/g, '&#91;$1]:');
    const raw = marked.parse(escapedDefs, { async: false, renderer: this._renderer }) as string;
    return DOMPurify.sanitize(raw);
  }
}

