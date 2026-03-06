/** Centralised localStorage key registry — every key used by the app must be declared here. */
export const StorageKeys = {
  displayMode: 'meta-anime:displayMode',
  notifications: 'meta-anime-notifications',
  filterPresets: 'meta-anime:filterPresets',
  seasonalFilterPresets: 'meta-anime:seasonalFilterPresets',
  autoApplyLastFilters: 'meta-anime:autoApplyLastFilters',
  lastUsedFilters: 'meta-anime:lastUsedFilters',
  luckyOnlyFinished: 'meta-anime:luckyOnlyFinished',
  luckyOnlyPlanToWatch: 'meta-anime:luckyOnlyPlanToWatch',
  luckyOnlyWithFiles: 'meta-anime:luckyOnlyWithFiles',
  language: 'meta-anime:language',
} as const;
