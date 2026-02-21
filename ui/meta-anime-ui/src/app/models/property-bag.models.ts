export interface PropertyBagResponse {
  key: string;
  value: string;
  creationDate: string;
  updateDate: string;
}

export interface UpsertPropertyBagRequest {
  value: string;
}
