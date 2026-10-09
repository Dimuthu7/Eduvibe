import { Subject, throwError } from 'rxjs';
import { Loadable } from './loadable';

describe('Loadable', () => {
  it('moves from loading to ready with the data', () => {
    const source = new Subject<string[]>();
    const list = new Loadable<string[]>();

    list.load(source);
    expect(list.status()).toBe('loading');

    source.next(['a']);
    expect(list.status()).toBe('ready');
    expect(list.data()).toEqual(['a']);
  });

  it('keeps the error when loading fails', () => {
    const list = new Loadable<string[]>();

    list.load(throwError(() => new Error('boom')));

    expect(list.status()).toBe('error');
    expect(list.error()).toBeInstanceOf(Error);
  });

  it('updates loaded data in place and ignores updates before loading', () => {
    const list = new Loadable<number[]>();
    list.update((n) => [...n, 1]);
    expect(list.data()).toBeUndefined();

    list.load(new Subject<number[]>().pipe());
    const source = new Subject<number[]>();
    list.load(source);
    source.next([1]);
    list.update((n) => [...n, 2]);

    expect(list.data()).toEqual([1, 2]);
  });
});
