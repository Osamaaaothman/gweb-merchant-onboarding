import { AsyncLocalStorage } from "node:async_hooks";

export interface CorrelationContext {
  correlationId: string;
  applicationId?: string;
  handler: string;
}

// One execution environment handles one invocation at a time (AWS Lambda's execution
// model), so a module-level AsyncLocalStorage is safe: it never leaks context between
// concurrent requests the way a naive global variable would.
const storage = new AsyncLocalStorage<CorrelationContext>();

export function runWithCorrelationContext<T>(context: CorrelationContext, fn: () => T): T {
  return storage.run(context, fn);
}

export function getCorrelationContext(): CorrelationContext | undefined {
  return storage.getStore();
}
