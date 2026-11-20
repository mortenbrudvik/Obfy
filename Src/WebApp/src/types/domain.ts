// Shapes exchanged with the .NET side (Obfy.App request/response records and pushed events): camelCase JSON, string
// enums, Guid ids as strings. Keep each type next to the C# record it mirrors.

/** What `app.info` answers with (AppInfo). */
export interface AppInfo {
  version: string;
}
