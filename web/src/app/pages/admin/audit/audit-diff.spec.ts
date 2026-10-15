import { changesOf, spaced } from './audit-diff';

describe('changesOf', () => {
  it('shows an update as each changed field from its old value to its new one', () => {
    expect(changesOf('{"Status":"Active","Note":null}', '{"Status":"Left","Note":"moved"}')).toEqual([
      { field: 'Status', from: 'Active', to: 'Left' },
      { field: 'Note', from: null, to: 'moved' },
    ]);
  });

  it('shows a create as what was set and a delete as what was there', () => {
    expect(changesOf(null, '{"Amount":1500}')).toEqual([{ field: 'Amount', from: null, to: '1500' }]);
    expect(changesOf('{"Amount":1500}', null)).toEqual([{ field: 'Amount', from: '1500', to: null }]);
  });

  it('leaves out the housekeeping columns every row has', () => {
    expect(changesOf(null, '{"Id":"x","TenantId":"t","CreatedAt":"c","UpdatedAt":"u","Name":"Pro"}').map((c) => c.field)).toEqual(['Name']);
  });

  it('copes with nothing stored or text that is not JSON', () => {
    expect(changesOf(null, null)).toEqual([]);
    expect(changesOf('not json', '[1,2]')).toEqual([]);
  });

  it('writes booleans and numbers as plain text', () => {
    expect(changesOf('{"Enabled":false}', '{"Enabled":true}')).toEqual([{ field: 'Enabled', from: 'false', to: 'true' }]);
  });
});

describe('spaced', () => {
  it('splits a record name into words', () => {
    expect(spaced('PlanFeature')).toBe('Plan feature');
    expect(spaced('User')).toBe('User');
  });
});
