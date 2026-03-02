import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  signal,
} from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

export type HelpModalType = 'mal' | 'sonarr';

@Component({
  selector: 'app-help-modal',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  templateUrl: './help-modal.component.html',
  styleUrl: './help-modal.component.scss',
})
export class HelpModalComponent implements OnChanges {
  @Input({ required: true }) type!: HelpModalType;
  @Input() visible = false;
  /** The exact OAuth callback URL to display in the MAL help steps (e.g. window.location.origin + '/auth/mal/callback'). */
  @Input() callbackUrl?: string;
  @Output() closed = new EventEmitter<void>();

  protected leaving = signal(false);

  ngOnChanges(): void {
    if (!this.visible) {
      this.leaving.set(false);
    }
  }

  protected dismiss(): void {
    this.leaving.set(true);
    setTimeout(() => {
      this.leaving.set(false);
      this.closed.emit();
    }, 200);
  }
}
