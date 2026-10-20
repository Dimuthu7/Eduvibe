import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { TranslateService } from '../../core/i18n/translate.service';
import { MoveDialog, MoveDialogData } from './move-dialog';

describe('MoveDialog', () => {
  const close = vi.fn();

  const setup = () => {
    const data: MoveDialogData = { name: 'Week 1 notes', options: [{ value: 'Notes|', label: 'Notes' }, { value: 'Notes|t1', label: 'Notes / Term 1' }] };
    TestBed.configureTestingModule({
      imports: [MoveDialog],
      providers: [{ provide: MAT_DIALOG_DATA, useValue: data }, { provide: MatDialogRef, useValue: { close } }],
    });
    vi.spyOn(TestBed.inject(TranslateService), 'translate').mockImplementation((key) => key);
    const fixture = TestBed.createComponent(MoveDialog);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement, dialog: fixture.componentInstance as unknown as { to: { setValue(v: string): void } } };
  };

  afterEach(() => close.mockReset());

  it('names what is being moved and cannot move until a place is chosen', () => {
    const { el } = setup();
    expect(el.querySelector('.name')!.textContent).toContain('Week 1 notes');
    const move = Array.from(el.querySelectorAll('button')).find((b) => b.textContent!.includes('materials.move'))!;
    expect(move.disabled).toBe(true);
  });

  it('closes with the chosen place', () => {
    const { fixture, el, dialog } = setup();
    dialog.to.setValue('Notes|t1');
    fixture.detectChanges();
    const move = Array.from(el.querySelectorAll('button')).find((b) => b.textContent!.includes('materials.move'))!;
    expect(move.disabled).toBe(false);
    move.click();
    expect(close).toHaveBeenCalledWith('Notes|t1');
  });
});
