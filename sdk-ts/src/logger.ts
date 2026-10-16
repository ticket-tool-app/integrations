export interface SdkLogger {
  debug(message: string): void;
  info(message: string): void;
  warn(message: string, error?: unknown): void;
}

export const silentLogger: SdkLogger = {
  debug() {},
  info() {},
  warn() {},
};
