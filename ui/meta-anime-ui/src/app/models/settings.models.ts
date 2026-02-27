export interface SettingDefinition {
  key: string;
  category: string;
  isSensitive: boolean;
  isRequired: boolean;
  value: string | null;
  fieldType: string;
  selectOptions?: string[];
}

export interface UpdateSettingRequest {
  value: string;
}

export type SettingsByCategory = Record<string, SettingDefinition[]>;
