import { Injectable, inject, signal } from '@angular/core';
import { forkJoin, Observable } from 'rxjs';
import { PropertyBagService } from './property-bag.service';
import {
  DEFAULT_HANDBRAKE_PRESET_NAME,
  DEFAULT_PATH_MAPPING,
  HandBrakeDestMode,
  isMappedPath,
  PathSeparatorStyle,
  resolveUserPath,
  UserPathMapping,
} from '../utils/user-path.utils';

const KEYS = {
  containerRoot: 'PathMap:ContainerRoot',
  hostRoot: 'PathMap:HostRoot',
  separator: 'PathMap:Separator',
  handBrakePresetName: 'HandBrake:PresetName',
  handBrakeDestDir: 'HandBrake:DestDir',
  handBrakeDestMode: 'HandBrake:DestMode',
} as const;

/**
 * Settings-backed access to the container→host path mapping and the HandBrake
 * export configuration (managed on the App Preferences page). resolveUserPath()
 * here is the single entry point used by every display/copy/export path in the
 * Library drawer.
 */
@Injectable({ providedIn: 'root' })
export class UserPathService {
  private readonly propertyBag = inject(PropertyBagService);

  readonly mapping = signal<UserPathMapping>({ ...DEFAULT_PATH_MAPPING });
  readonly presetName = signal<string>(DEFAULT_HANDBRAKE_PRESET_NAME);
  readonly destDir = signal<string>('');
  readonly destMode = signal<HandBrakeDestMode>('transcoded');
  readonly loaded = signal(false);

  constructor() {
    this.load();
  }

  load(): void {
    forkJoin({
      containerRoot: this.propertyBag.get(KEYS.containerRoot),
      hostRoot: this.propertyBag.get(KEYS.hostRoot),
      separator: this.propertyBag.get(KEYS.separator),
      presetName: this.propertyBag.get(KEYS.handBrakePresetName),
      destDir: this.propertyBag.get(KEYS.handBrakeDestDir),
      destMode: this.propertyBag.get(KEYS.handBrakeDestMode),
    }).subscribe({
      next: r => {
        this.mapping.set({
          containerRoot: r.containerRoot?.value?.trim() || DEFAULT_PATH_MAPPING.containerRoot,
          hostRoot: r.hostRoot?.value?.trim() ?? '',
          separator: (r.separator?.value === 'windows' ? 'windows' : 'posix') as PathSeparatorStyle,
        });
        this.presetName.set(r.presetName?.value?.trim() || DEFAULT_HANDBRAKE_PRESET_NAME);
        this.destDir.set(r.destDir?.value?.trim() ?? '');
        this.destMode.set(r.destMode?.value === 'dir' ? 'dir' : 'transcoded');
        this.loaded.set(true);
      },
      error: () => {
        // Property bag unreachable (e.g. before login) — defaults stand.
        this.loaded.set(true);
      },
    });
  }

  /** Persists a partial path mapping (App Preferences card). */
  save(patch: Partial<UserPathMapping>): Observable<unknown> {
    const next: UserPathMapping = { ...this.mapping(), ...patch };
    this.mapping.set(next);

    const calls: Observable<unknown>[] = [];
    if (patch.containerRoot !== undefined) {
      calls.push(this.propertyBag.upsert(KEYS.containerRoot, { value: next.containerRoot }));
    }
    if (patch.hostRoot !== undefined) {
      calls.push(this.propertyBag.upsert(KEYS.hostRoot, { value: next.hostRoot }));
    }
    if (patch.separator !== undefined) {
      calls.push(this.propertyBag.upsert(KEYS.separator, { value: next.separator }));
    }
    return calls.length ? forkJoin(calls) : new Observable(o => o.complete());
  }

  /** Persists the HandBrake export configuration (App Preferences card). */
  saveHandBrake(patch: { presetName?: string; destDir?: string; destMode?: HandBrakeDestMode }): Observable<unknown> {
    if (patch.presetName !== undefined) this.presetName.set(patch.presetName);
    if (patch.destDir !== undefined) this.destDir.set(patch.destDir);
    if (patch.destMode !== undefined) this.destMode.set(patch.destMode);

    const calls: Observable<unknown>[] = [];
    if (patch.presetName !== undefined) {
      calls.push(this.propertyBag.upsert(KEYS.handBrakePresetName, { value: this.presetName() }));
    }
    if (patch.destDir !== undefined) {
      calls.push(this.propertyBag.upsert(KEYS.handBrakeDestDir, { value: this.destDir() }));
    }
    if (patch.destMode !== undefined) {
      calls.push(this.propertyBag.upsert(KEYS.handBrakeDestMode, { value: this.destMode() }));
    }
    return calls.length ? forkJoin(calls) : new Observable(o => o.complete());
  }

  /** Container path → local host path (identity when unmapped). */
  resolve(containerPath: string): string {
    return resolveUserPath(containerPath, this.mapping());
  }

  /** True when the path was actually rewritten (drives the 'Mapped to Host Path' badge). */
  isMapped(containerPath: string): boolean {
    return isMappedPath(containerPath, this.mapping());
  }
}
