import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { canRetryLoad, loadErrorKey } from './load-error';

const failure = (status: number, code?: string) => new HttpErrorResponse({ status, error: code ? { code } : null });

describe('loadErrorKey', () => {
  it('says the plan does not include it, instead of blaming the connection', () => {
    expect(loadErrorKey(failure(403, 'plan_feature_locked'))).toBe('error.plan_feature_locked');
    expect(canRetryLoad(failure(403, 'plan_feature_locked'))).toBe(false);
  });

  it('blames the connection when the server could not be reached', () => {
    expect(loadErrorKey(failure(0))).toBe('error.load_failed');
    expect(canRetryLoad(failure(0))).toBe(true);
  });

  it('says it was the server for a server failure', () => {
    expect(loadErrorKey(failure(502))).toBe('error.server');
    expect(canRetryLoad(failure(502))).toBe(true);
  });

  it('offers no retry when the person may not see it', () => {
    expect(loadErrorKey(failure(403))).toBe('error.forbidden');
    expect(canRetryLoad(failure(403))).toBe(false);
  });

  it('falls back to the load message for anything else', () => {
    expect(loadErrorKey(new Error('boom'))).toBe('error.load_failed');
    expect(loadErrorKey(null)).toBe('error.load_failed');
  });
});
