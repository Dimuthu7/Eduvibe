import { of, throwError } from 'rxjs';
import { formatSize, materialIcon, openMaterial } from './material-view';

describe('material-view', () => {
  it('writes sizes a person can read', () => {
    expect(formatSize(300)).toBe('300 B');
    expect(formatSize(1)).toBe('1 B');
    expect(formatSize(1500)).toBe('1 KB');
    expect(formatSize(340 * 1024)).toBe('340 KB');
    expect(formatSize(2.4 * 1024 * 1024)).toBe('2.4 MB');
  });

  it('picks an icon by file type', () => {
    expect(materialIcon('application/pdf')).toBe('picture_as_pdf');
    expect(materialIcon('image/png')).toBe('image');
    expect(materialIcon('application/vnd.openxmlformats-officedocument.presentationml.presentation')).toBe('slideshow');
    expect(materialIcon('application/vnd.ms-excel')).toBe('table_chart');
    expect(materialIcon('text/plain')).toBe('description');
  });

  describe('openMaterial', () => {
    const tab = { location: { href: '' }, close: vi.fn() };
    // saveBlob clicks a hidden link; what it was told to save as is what the person's phone would call the file.
    const saved: string[] = [];

    beforeEach(() => {
      tab.location.href = '';
      tab.close.mockReset();
      saved.length = 0;
      vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
        saved.push(this.download);
      });
      vi.spyOn(window, 'open').mockReturnValue(tab as unknown as Window);
      URL.createObjectURL = vi.fn(() => 'blob:file');
      URL.revokeObjectURL = vi.fn();
    });

    afterEach(() => vi.restoreAllMocks());

    it('opens the tab before asking for the file, then shows the file in it', () => {
      const asked: boolean[] = [];
      const finished = vi.fn();
      openMaterial((download) => (asked.push(download), of(new Blob(['x']))), 'paper.pdf', true, finished);
      expect(window.open).toHaveBeenCalledWith('', '_blank');
      expect(asked).toEqual([false]);
      expect(tab.location.href).toBe('blob:file');
      expect(saved).toEqual([]);
      expect(finished).toHaveBeenCalledWith();
    });

    it('downloads a file the browser cannot show, without opening a tab', () => {
      const asked: boolean[] = [];
      openMaterial((download) => (asked.push(download), of(new Blob(['x']))), 'notes.docx', false, vi.fn());
      expect(window.open).not.toHaveBeenCalled();
      expect(asked).toEqual([true]);
      expect(saved).toEqual(['notes.docx']);
    });

    it('downloads instead when the phone blocked the tab', () => {
      vi.spyOn(window, 'open').mockReturnValue(null);
      openMaterial(() => of(new Blob(['x'])), 'paper.pdf', true, vi.fn());
      expect(saved).toEqual(['paper.pdf']);
    });

    it('closes the empty tab and reports the error when the file cannot be fetched', () => {
      const finished = vi.fn();
      const failure = new Error('nope');
      openMaterial(() => throwError(() => failure), 'paper.pdf', true, finished);
      expect(tab.close).toHaveBeenCalled();
      expect(finished).toHaveBeenCalledWith(failure);
    });
  });
});
