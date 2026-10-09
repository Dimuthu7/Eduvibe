import { LoadingService } from './loading.service';

describe('LoadingService', () => {
  it('stays busy until every request has finished', () => {
    const loading = new LoadingService();

    loading.start();
    loading.start();
    loading.stop();
    expect(loading.busy()).toBe(true);

    loading.stop();
    expect(loading.busy()).toBe(false);
  });

  it('never goes below zero', () => {
    const loading = new LoadingService();

    loading.stop();
    loading.start();

    expect(loading.busy()).toBe(true);
  });
});
