/** Centralised localStorage key registry — every key used by the app must be declared here. */
export const StorageKeys = {
  displayMode: 'meta-anime:displayMode',
  notifications: 'meta-anime-notifications',
  filterPresets: 'meta-anime:filterPresets',
  seasonalFilterPresets: 'meta-anime:seasonalFilterPresets',
  autoApplyLastFilters: 'meta-anime:autoApplyLastFilters',
  seasonalSuggestionScope: 'meta-anime:seasonalSuggestionScope',
  seasonalExcludeDonghua: 'meta-anime:seasonalExcludeDonghua',
  seasonalExcludedGenres: 'meta-anime:seasonalExcludedGenres',
  seasonalForYouFilters: 'meta-anime:seasonalForYouFilters',
  lastUsedFilters: 'meta-anime:lastUsedFilters',
  seasonalLastUsedFilters: 'meta-anime:seasonalLastUsedFilters',
  luckyOnlyFinished: 'meta-anime:luckyOnlyFinished',
  luckyOnlyPlanToWatch: 'meta-anime:luckyOnlyPlanToWatch',
  luckyOnlyWithFiles: 'meta-anime:luckyOnlyWithFiles',
  language: 'meta-anime:language',
  welcomeShown: 'meta-anime:welcomeShown',
} as const;
