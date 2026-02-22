export interface SettingDefinition {
  key: string;
  category: string;
  displayName: string;
  description: string;
  isSensitive: boolean;
  isRequired: boolean;
  value: string | null;
}

export interface UpdateSettingRequest {
  value: string;
}

export type SettingsByCategory = Record<string, SettingDefinition[]>;
