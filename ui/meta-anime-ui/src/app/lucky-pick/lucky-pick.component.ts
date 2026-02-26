import {
  Component,
  inject,
  signal,
  computed,
  OnInit,
  OnDestroy,
  HostListener,
  Output,
  EventEmitter,
  ChangeDetectionStrategy,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription } from 'rxjs';
import { AnimeService } from '../services/anime.service';
import { UserPreferencesService } from '../services/user-preferences.service';
import { AnimeDetailsResponse, AnimeFilters } from '../models/anime.models';

type LuckyState = 'idle' | 'spinning' | 'result' | 'no-match' | 'error';

/** Bilingual phrase bank — one array per supported locale. */
interface LangPhrases {
  en: readonly string[];
  fr: readonly string[];
}

// ── Humorous copy banks (EN + FR, 60 phrases each) ───────────────────────────
const PLEX_PHRASES: LangPhrases = {
  en: [
    'Summon the popcorn!',
    'Let the binge begin!',
    'Stream it before your resolve crumbles.',
    'Your sofa is calling.',
    'Skip sleep. Start streaming.',
    'Deploy the watching station!',
    'One does not simply watch one episode.',
    'Challenge accepted — marathon mode: ON.',
    'Lights, snacks, existential dread. Go!',
    'Your screen awaits, weeb.',
    'Hit play. Regret nothing.',
    'The remote is destiny.',
    "Tonight's agenda: this anime, snacks, repeat.",
    'Your pillow is already in position.',
    'Begin the sacred stream.',
    "Queued. Buffered. Ready to ruin your sleep schedule.",
    "Auto-play is not a suggestion, it's a lifestyle.",
    'Social plans? Cancelled. This is better.',
    'The opening theme alone is worth it.',
    'Lean back. Your couch is a cinema now.',
    "One episode. I swear. (It's a lie.)",
    "Stream responsibly. Just kidding — marathon it.",
    'Your watchlist thanks you.',
    'The couch fortress must be assembled.',
    "No subtitles needed — you'll figure it out.",
    'Press play and let fate guide you.',
    'Your future self will thank you for starting this.',
    'Sleep is a myth. Press play.',
    'You deserve this watch session.',
    'The plot thickens — go watch.',
    'Your blanket is already expecting you.',
    'Skip the trailer. Trust the RNG.',
    'The opening banger awaits.',
    'Full screen. Headphones. Go.',
    "It's not procrastination, it's watching anime.",
    'Consider this your sign to watch it.',
    'The streaming gods have chosen wisely.',
    'Plot armour: maximum. Fear: zero.',
    'Your next binge is already loading.',
    'Darkness is falling. Plex is calling.',
    'This is the way. The Plex way.',
    'All hail the recommendation algorithm.',
    'The saga begins now.',
    "Watch it before the internet spoils it.",
    'You asked for a sign. This is it.',
    "Episode one or season three, we don't judge.",
    'Close the browser, open Plex.',
    'The controller is in your hands. The destiny too.',
    'Your DND status is now "streaming".',
    'A new adventure awaits on your screen.',
    'The episode count is merely a suggestion.',
    "Don't check the time. Just watch.",
    'The Plex library is richer today.',
    'Load the next masterpiece.',
    'Queue it up. No regrets.',
    'This anime chose YOU.',
    'Time to disappear into another world.',
    'Your viewing history needs updating.',
    "Anime doesn't watch itself — but almost.",
    'Tonight, you become the main character.',
  ],
  fr: [
    'Convoque le popcorn !',
    'Que le binge commence !',
    'Lance le streaming avant que ta volonté ne craque.',
    'Ton canapé t\'attend.',
    'Oublie le sommeil. Lance le stream.',
    'Déploie la station de visionnage !',
    'On ne regarde pas juste un épisode, c\'est prouvé.',
    'Défi accepté — mode marathon : ACTIVÉ.',
    'Lumières, snacks, crise existentielle. C\'est parti !',
    'Ton écran t\'attend, weeb.',
    'Lance et ne regrette rien.',
    'La télécommande, c\'est le destin.',
    'Au programme ce soir : cet anime, snacks, recommencer.',
    'Ton oreiller est déjà en position.',
    'Commence le stream sacré.',
    "En file d'attente. En mémoire tampon. Prêt à ruiner ton planning sommeil.",
    "La lecture automatique n'est pas une suggestion, c'est un mode de vie.",
    'Plans sociaux ? Annulés. C\'est mieux ainsi.',
    'Le générique d\'ouverture seul vaut le détour.',
    'Installe-toi. Ton canapé est un cinéma maintenant.',
    'Un épisode. Je le jure. (C\'est un mensonge.)',
    'Streame responsablement. Ah non — fais-en un marathon.',
    'Ta liste de visionnage te remercie.',
    'La forteresse canapé doit être assemblée.',
    'Pas besoin de sous-titres — tu te débrouilleras.',
    'Appuie sur lecture et laisse le destin guider.',
    'Ton futur moi te remerciera d\'avoir commencé ça.',
    'Le sommeil est un mythe. Appuie sur lecture.',
    'Tu mérites cette session de visionnage.',
    'L\'intrigue s\'épaissit — va regarder.',
    'Ta couverture t\'attend déjà.',
    'Passe la bande-annonce. Fais confiance au RNG.',
    'L\'opening banger t\'attend.',
    'Plein écran. Écouteurs. C\'est parti.',
    'Ce n\'est pas de la procrastination, c\'est regarder des animes.',
    'Considère ça comme un signe pour le regarder.',
    'Les dieux du streaming ont bien choisi.',
    'Armure narrative : maximale. Peur : zéro.',
    'Ton prochain binge est déjà en chargement.',
    'La nuit tombe. Plex appelle.',
    'C\'est la voie. La voie Plex.',
    'Gloire à l\'algorithme de recommandation.',
    'La saga commence maintenant.',
    'Regarde-le avant qu\'internet ne spoile tout.',
    'Tu cherchais un signe. Le voilà.',
    'Épisode un ou saison trois, on ne juge pas.',
    'Ferme le navigateur, ouvre Plex.',
    'La manette est dans tes mains. Le destin aussi.',
    'Ton statut DND est maintenant "en streaming".',
    'Une nouvelle aventure t\'attend sur ton écran.',
    'Le nombre d\'épisodes est une simple suggestion.',
    'Ne regarde pas l\'heure. Regarde juste.',
    'La bibliothèque Plex est plus riche aujourd\'hui.',
    'Lance le prochain chef-d\'œuvre.',
    'Mets-le en file d\'attente. Sans regret.',
    'Cet anime T\'a choisi.',
    'L\'heure de disparaître dans un autre monde.',
    'Ton historique de visionnage a besoin d\'une mise à jour.',
    'L\'anime ne se regarde pas tout seul — presque.',
    'Ce soir, tu deviens le personnage principal.',
  ],
};

const MAL_PHRASES: LangPhrases = {
  en: [
    'Add to the endless backlog.',
    'Register this into the void.',
    'Feed the algorithm.',
    'Pad that Plan-to-Watch list.',
    'Witness the MAL score drama.',
    'Judge it with numbers.',
    'Let strangers rate your taste.',
    '10/10 it before watching.',
    'Confirm it is, indeed, an anime.',
    'Log it for future abandonment.',
    'Your backlog grows ever mightier.',
    'Stamp it with a status.',
    'The list demands tribute.',
    'Add it before you forget it exists.',
    'Update your anime curriculum.',
    'Your profile needs this entry.',
    'Let the MAL score gods decide.',
    "It's not hoarding if it's anime.",
    'A wild entry has appeared!',
    'Claim your database spot.',
    'Track it or it never happened.',
    'Catalogue the chaos.',
    'The void of Plan-to-Watch deepens.',
    'Your collection gains a member.',
    'Register before the backlog forgets.',
    'Update that list, you hoarder.',
    'Leave a review. No one will read it.',
    'Every anime deserves a data point.',
    'Score it. Rate it. Abandon it.',
    'Your statistics need enriching.',
    'Mark it. Then never watch it.',
    'MAL awaits your sacred entry.',
    'More entries = more power.',
    'The algorithm hungers for data.',
    'Feed the recommendation engine.',
    "Don't ghost MAL. It loves you.",
    'Bestow upon it a number rating.',
    'The list is never complete. Add it.',
    'Document this for posterity.',
    'Signal your interest to the database.',
    'Your MAL score history is watching.',
    'It counts as research. Log it.',
    'The spreadsheet of fate grows.',
    'Expand the empire of unwatched anime.',
    'Add now. Judge never.',
    'One more entry, one step closer to completion.',
    'Your watched count needs this win.',
    'MAL is your anime passport. Stamp it.',
    'Drop it at episode 3 later. Log it now.',
    'The internet needs to know your opinion.',
    'Build the perfect plan-to-watch prison.',
    'Your future self will appreciate the log.',
    'Archive the adventure before it fades.',
    'Even plan-to-watch counts. Add it.',
    'The score awaits its destiny.',
    'Log it louder for the people in the back.',
    'Tick it. Tag it. Type a review.',
    'Another entry. Another day. Another regret.',
    'The database appreciates your service.',
    'Mark it on the map of your anime life.',
  ],
  fr: [
    'Ajoute au backlog infini.',
    'Enregistre-le dans le vide.',
    'Nourris l\'algorithme.',
    'Gonfle cette liste "À regarder".',
    'Assiste au drame des scores MAL.',
    'Juge-le avec des chiffres.',
    'Laisse des inconnus noter ton goût.',
    'Mets 10/10 avant même de regarder.',
    'Confirme que c\'est bien un anime.',
    'Logue-le pour l\'abandonner plus tard.',
    'Ton backlog gagne en puissance.',
    'Tamponne-le avec un statut.',
    'La liste exige son tribut.',
    'Ajoute-le avant de l\'oublier.',
    'Mets à jour ton curriculum anime.',
    'Ton profil a besoin de cette entrée.',
    'Laisse les dieux du score MAL décider.',
    'C\'est pas de l\'accumulation si c\'est des animes.',
    'Une entrée sauvage est apparue !',
    'Revendique ta place dans la base de données.',
    'Logue-le ou il n\'a jamais existé.',
    'Catalogue le chaos.',
    'Le vide de "À regarder" s\'approfondit.',
    'Ta collection accueille un nouveau membre.',
    'Enregistre avant que le backlog oublie.',
    'Mets à jour cette liste, collectionneur.',
    'Laisse une critique. Personne ne la lira.',
    'Chaque anime mérite un point de données.',
    'Note-le. Évalue-le. Abandonne-le.',
    'Tes statistiques ont besoin d\'enrichissement.',
    'Marque-le. Puis ne le regarde jamais.',
    'MAL attend ton entrée sacrée.',
    'Plus d\'entrées = plus de puissance.',
    'L\'algorithme a faim de données.',
    'Nourris le moteur de recommandation.',
    'Ne ghoste pas MAL. Il t\'aime.',
    'Accorde-lui une note numérique.',
    'La liste n\'est jamais complète. Ajoute-le.',
    'Documente ça pour la postérité.',
    'Signale ton intérêt à la base de données.',
    'Ton historique de scores MAL te surveille.',
    'Ça compte comme de la recherche. Logue-le.',
    'La feuille de calcul du destin s\'étend.',
    'Étends l\'empire des animes non regardés.',
    'Ajoute maintenant. Juge jamais.',
    'Une entrée de plus, un pas vers la complétion.',
    'Ton compteur de vus a besoin de cette victoire.',
    'MAL est ton passeport anime. Tamponné.',
    'Abandonne-le à l\'épisode 3 plus tard. Logue-le maintenant.',
    'Internet a besoin de connaître ton avis.',
    'Construis la prison parfaite de "À regarder".',
    'Ton futur moi appréciera le log.',
    'Archive l\'aventure avant qu\'elle disparaisse.',
    'Même "À regarder" compte. Ajoute-le.',
    'Le score attend son destin.',
    'Logue-le plus fort pour ceux au fond.',
    'Coche. Étiquette. Tape une critique.',
    'Une entrée de plus. Un jour de plus. Un regret de plus.',
    'La base de données apprécie ton service.',
    'Marque-le sur la carte de ta vie anime.',
  ],
};

const REROLL_PHRASES: LangPhrases = {
  en: [
    'Trash tier, roll again!',
    'Nope. Next!',
    'The prophecy demands a reroll.',
    'This one... is not it.',
    'Call upon the dice spirits!',
    'Fortune spins twice for cowards.',
    'One more spin of destiny.',
    'Rejected. Spin the wheel!',
    '404: hype not found. Retry.',
    'The RNG gods demand sacrifice.',
    'Skip! Next adventure awaits.',
    'The dice have spoken — again.',
    'Chaos demands another spin.',
    'Insufficient hype. Rerolling.',
    'Not today. Try the next fate.',
    'The stars are not aligned. Respin.',
    'Hard pass. Wheel, do your thing.',
    'Nah. Fate, try again.',
    'Roll it back and try again.',
    'Your destiny is one spin away.',
    'The RNG has failed you. Retry.',
    'Fortune favours the persistent.',
    'Not the chosen one. Spin again.',
    "This is not the anime you're looking for.",
    'The wheel of fate spins on.',
    'Bold move. Spin again anyway.',
    'Cast the dice into the void.',
    'Randomness has more to offer.',
    'Your true calling is one spin away.',
    'The universe has other plans. Roll again.',
    'Skip to the good stuff.',
    'The prophecy was wrong. Respin.',
    'Another spin, another chance at greatness.',
    'The dice gods are not done with you.',
    'One more roll. I believe in you.',
    'Chaotic neutral energy. Spin again.',
    "Plot twist: there's a better pick.",
    'The anime gods have other intentions.',
    'Turn the wheel of destiny once more.',
    "Meh. The RNG has hidden depths.",
    'Fortune does not tire of spinning.',
    'Refresh the fates.',
    'Discard and draw again.',
    'The dice are fickle. Roll once more.',
    'Nope. The wheel demands action.',
    'Retry. The algorithm has secrets.',
    'Your true destiny awaits the next spin.',
    'Chaos is a ladder. Climb it. Spin again.',
    'Insufficient destiny. Recompute.',
    'Hard skip. Fate reboot.',
    'Consult the oracle once more.',
    'Spin the wheel of wonder.',
    'Not quite. One more chance.',
    'Your fate file is corrupted. Reload.',
    'Destiny buffer overflow. Respin.',
    'The RNG apologises. It tries again.',
    'Wrong timeline. Recalculate.',
    'The dice gave 0/10. Try again.',
    'Nope nope nope. Next!',
    'Let chaos decide once more!',
  ],
  fr: [
    'Rang poubelle, rejoue !',
    'Non. Suivant !',
    'La prophétie exige un reroll.',
    'Celui-là... c\'est pas ça.',
    'Invoque les esprits du dé !',
    'La fortune tourne deux fois pour les lâches.',
    'Un autre tour du destin.',
    'Rejeté. Tourne la roue !',
    '404 : hype introuvable. Réessaie.',
    'Les dieux du RNG exigent un sacrifice.',
    'Passe ! La prochaine aventure t\'attend.',
    'Les dés ont parlé — encore.',
    'Le chaos exige un autre tour.',
    'Hype insuffisante. Reroll en cours.',
    'Pas aujourd\'hui. Essaie le prochain destin.',
    'Les astres ne sont pas alignés. Retourne.',
    'Non merci. Roue, fais ton boulot.',
    'Bof. Destin, recommence.',
    'Retourne en arrière et réessaie.',
    'Ton destin est à un spin.',
    'Le RNG t\'a trahi. Réessaie.',
    'La fortune sourit aux persistants.',
    'Pas l\'élu. Tourne encore.',
    'Ce n\'est pas l\'anime que tu cherches.',
    'La roue du destin continue de tourner.',
    'Mouvement audacieux. Retourne quand même.',
    'Lance les dés dans le vide.',
    'Le hasard a plus à offrir.',
    'Ta vraie vocation est à un spin.',
    'L\'univers a d\'autres plans. Relance.',
    'Passe au truc bien.',
    'La prophétie avait tort. Respin.',
    'Un tour de plus, une chance de grandeur.',
    'Les dieux des dés n\'en ont pas fini avec toi.',
    'Un tour de plus. J\'ai foi en toi.',
    'Énergie neutre chaotique. Tourne encore.',
    'Coup de théâtre : il y a un meilleur choix.',
    'Les dieux anime ont d\'autres intentions.',
    'Tourne la roue du destin une fois encore.',
    'Bof. Le RNG a des profondeurs cachées.',
    'La fortune ne se lasse pas de tourner.',
    'Rafraîchis les destins.',
    'Défausse et pioche à nouveau.',
    'Les dés sont capricieux. Relance.',
    'Non. La roue réclame de l\'action.',
    'Réessaie. L\'algorithme a des secrets.',
    'Ton vrai destin attend le prochain spin.',
    'Le chaos est une échelle. Grimpe. Retourne.',
    'Destin insuffisant. Recalcul en cours.',
    'Skip dur. Redémarrage du destin.',
    'Consulte l\'oracle encore une fois.',
    'Tourne la roue des merveilles.',
    'Pas tout à fait. Une chance de plus.',
    'Ton fichier destin est corrompu. Recharge.',
    'Débordement du tampon destin. Respin.',
    'Le RNG présente ses excuses. Il réessaie.',
    'Mauvaise chronologie. Recalcul.',
    'Les dés ont donné 0/10. Recommence.',
    'Non non non. Suivant !',
    'Laisse le chaos décider encore !',
  ],
};

@Component({
  selector: 'app-lucky-pick',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, RouterLink],
  templateUrl: './lucky-pick.component.html',
  styleUrl: './lucky-pick.component.scss',
})
export class LuckyPickComponent implements OnInit, OnDestroy {
  @Output() closed = new EventEmitter<void>();

  private animeService = inject(AnimeService);
  protected userPrefs = inject(UserPreferencesService);
  private translate = inject(TranslateService);

  protected state = signal<LuckyState>('idle');
  protected result = signal<AnimeDetailsResponse | null>(null);

  /** Randomly-selected humorous button phrases, rerolled on every spin */
  protected plexPhrase   = signal(this.phrases(PLEX_PHRASES));
  protected malPhrase    = signal(this.phrases(MAL_PHRASES));
  protected rerollPhrase = signal(this.phrases(REROLL_PHRASES));

  /** True if the OS prefers reduced motion */
  protected readonly prefersReducedMotion = this.checkReducedMotion();

  private checkReducedMotion(): boolean {
    try {
      return typeof window !== 'undefined' &&
        typeof window.matchMedia === 'function'
        ? window.matchMedia('(prefers-reduced-motion: reduce)').matches
        : false;
    } catch {
      return false;
    }
  }

  /** Spin duration in ms — reduced when prefersReducedMotion */
  private readonly SPIN_MS = this.prefersReducedMotion ? 0 : 2200;
  private spinTimer: ReturnType<typeof setTimeout> | null = null;
  private langSub: Subscription | null = null;

  protected isSpinning = computed(() => this.state() === 'spinning');
  protected isResult = computed(() => this.state() === 'result');
  protected isNoMatch = computed(() => this.state() === 'no-match');
  protected isError = computed(() => this.state() === 'error');

  // ── Getters for result card ───────────────────────────────────────────────

  protected get anime(): AnimeDetailsResponse | null {
    return this.result();
  }

  protected get coverImageUrl(): string | undefined {
    return this.anime?.malAnimes?.[0]?.coverImageUrl;
  }

  protected get title(): string {
    const a = this.anime;
    if (!a) return '';
    const en = a.malAnimes?.[0]?.alternativeTitles?.en;
    return en ?? a.title;
  }

  protected get synopsis(): string {
    const a = this.anime;
    if (!a) return '';
    return a.malAnimes?.[0]?.synopsis ?? a.description ?? '';
  }

  protected get malUrl(): string | null {
    return this.anime?.malAnimes?.[0]?.url ?? null;
  }

  protected get plexUrl(): string | null {
    return this.anime?.plexAnimes?.[0]?.url ?? null;
  }

  // ── Lifecycle ─────────────────────────────────────────────────────────────

  ngOnInit(): void {
    // Re-roll phrases when the user switches language so copy matches locale
    this.langSub = this.translate.onLangChange.subscribe(() => {
      this.plexPhrase.set(this.phrases(PLEX_PHRASES));
      this.malPhrase.set(this.phrases(MAL_PHRASES));
      this.rerollPhrase.set(this.phrases(REROLL_PHRASES));
    });
    this.spin();
  }

  // ── Spin trigger ─────────────────────────────────────────────────────────

  spin(): void {
    if (this.state() === 'spinning') return;
    this.result.set(null);
    this.state.set('spinning');

    // Reroll funny phrases on every spin so the copy feels fresh
    this.plexPhrase.set(this.phrases(PLEX_PHRASES));
    this.malPhrase.set(this.phrases(MAL_PHRASES));
    this.rerollPhrase.set(this.phrases(REROLL_PHRASES));

    const filters: AnimeFilters = {};

    if (this.userPrefs.luckyOnlyPlanToWatch()) {
      filters.status = ['plan_to_watch'];
    }

    if (this.userPrefs.luckyOnlyFinished()) {
      filters.airingStatus = ['finished_airing'];
    }

    if (this.userPrefs.luckyOnlyWithFiles()) {
      filters.hasFile = true;
    }

    // Fetch first page to know total count, then pick a random page/item
    this.animeService.getAnimes(1, 1, filters).subscribe({
      next: (firstPage) => {
        const total = firstPage.totalCount;
        if (total === 0) {
          this.finishSpin('no-match');
          return;
        }
        // Pick a random item index
        const randomIndex = Math.floor(Math.random() * total);
        const page = randomIndex + 1; // pageSize=1 → page N = item N
        this.animeService.getAnimes(page, 1, filters).subscribe({
          next: (page2) => {
            const picked = page2.data[0] ?? null;
            this.result.set(picked);
            this.finishSpin(picked ? 'result' : 'no-match');
          },
          error: () => this.finishSpin('error'),
        });
      },
      error: () => this.finishSpin('error'),
    });
  }

  private finishSpin(nextState: LuckyState): void {
    if (this.SPIN_MS > 0) {
      this.spinTimer = setTimeout(() => this.state.set(nextState), this.SPIN_MS);
    } else {
      this.state.set(nextState);
    }
  }

  close(): void {
    this.clearTimer();
    this.closed.emit();
  }

  tryAgain(): void {
    this.spin();
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.close();
  }

  ngOnDestroy(): void {
    this.clearTimer();
    this.langSub?.unsubscribe();
  }

  private clearTimer(): void {
    if (this.spinTimer) {
      clearTimeout(this.spinTimer);
      this.spinTimer = null;
    }
  }

  /**
   * Picks a random phrase from the bank for the currently active locale.
   * Falls back to English if the locale is not explicitly supported.
   */
  private phrases(bank: LangPhrases): string {
    const lang = this.translate.currentLang ?? 'en';
    const arr: readonly string[] = bank[lang as keyof LangPhrases] ?? bank.en;
    return LuckyPickComponent.pickRandom(arr);
  }

  /** Picks a uniformly-random element from a readonly array. */
  private static pickRandom<T>(arr: readonly T[]): T {
    return arr[Math.floor(Math.random() * arr.length)];
  }
}
