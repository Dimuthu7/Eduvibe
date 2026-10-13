import { fileNamePart } from './download';
import { ReceiptData, ReceiptLabels, receiptRows, shareOrSave } from './receipt';

const labels: ReceiptLabels = {
  title: 'Payment receipt', receiptNo: 'Receipt no.', student: 'Student', className: 'Class', paidBy: 'Paid by',
  date: 'Date', months: 'For', amount: 'Amount received', thanks: 'Thank you',
};

const data: ReceiptData = {
  teacher: 'Sunil Perera', className: 'Grade 6 ICT', student: 'Amara Silva', receiptNo: 'R-000012',
  amount: 'LKR 1,500', method: 'Cash', paidOn: 'Mon, 12 Oct 2026', months: 'Oct 2026, Nov 2026',
};

describe('receiptRows', () => {
  it('lists the receipt number, student, class, date, method and months in order', () => {
    expect(receiptRows(data, labels)).toEqual([
      { label: 'Receipt no.', value: 'R-000012' },
      { label: 'Student', value: 'Amara Silva' },
      { label: 'Class', value: 'Grade 6 ICT' },
      { label: 'Date', value: 'Mon, 12 Oct 2026' },
      { label: 'Paid by', value: 'Cash' },
      { label: 'For', value: 'Oct 2026, Nov 2026' },
    ]);
  });

  it('leaves out the months line when there are no months', () => {
    expect(receiptRows({ ...data, months: '' }, labels).map((r) => r.label)).not.toContain('For');
  });
});

describe('shareOrSave', () => {
  const blob = new Blob(['x'], { type: 'image/png' });
  const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);

  beforeEach(() => {
    click.mockClear();
    URL.createObjectURL = vi.fn(() => 'blob:test');
    URL.revokeObjectURL = vi.fn();
  });

  afterEach(() => {
    Reflect.deleteProperty(navigator, 'canShare');
    Reflect.deleteProperty(navigator, 'share');
  });

  const mockShare = (share: () => Promise<void>) => {
    Object.defineProperty(navigator, 'canShare', { value: () => true, configurable: true });
    Object.defineProperty(navigator, 'share', { value: share, configurable: true });
  };

  it('opens the share sheet when the phone can share files', async () => {
    const share = vi.fn(() => Promise.resolve());
    mockShare(share);
    expect(await shareOrSave(blob, 'receipt.png', 'Receipt')).toBe('shared');
    expect(share).toHaveBeenCalledOnce();
    expect(click).not.toHaveBeenCalled();
  });

  it('saves the file when sharing files is not possible', async () => {
    expect(await shareOrSave(blob, 'receipt.png', 'Receipt')).toBe('saved');
    expect(click).toHaveBeenCalledOnce();
  });

  it('does nothing more when the person closes the share sheet', async () => {
    mockShare(() => Promise.reject(new DOMException('closed', 'AbortError')));
    expect(await shareOrSave(blob, 'receipt.png', 'Receipt')).toBe('cancelled');
    expect(click).not.toHaveBeenCalled();
  });

  it('saves the file when sharing fails for another reason', async () => {
    mockShare(() => Promise.reject(new DOMException('no', 'NotAllowedError')));
    expect(await shareOrSave(blob, 'receipt.png', 'Receipt')).toBe('saved');
    expect(click).toHaveBeenCalledOnce();
  });
});

describe('fileNamePart', () => {
  it('keeps letters and digits of any language and turns the rest into single hyphens', () => {
    expect(fileNamePart('Fees 2027 A/L Maths: 2026-10')).toBe('Fees-2027-A-L-Maths-2026-10');
    expect(fileNamePart('ගණිතය ශ්‍රේණිය 6')).toContain('ගණිතය');
    expect(fileNamePart('  ///  ')).toBe('file');
  });
});
