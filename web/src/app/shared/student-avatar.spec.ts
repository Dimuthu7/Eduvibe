import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { StudentPhotoEntry } from '../core/api/students.service';
import { AppConfig } from '../core/config/app-config';
import { initialsOf, StudentAvatar, toneOf } from './student-avatar';

const API = 'http://api.test';

@Component({
  imports: [StudentAvatar],
  template: `<app-student-avatar [name]="name" [of]="of" [badge]="badge" />`,
})
class Host {
  name = 'Kasun Perera';
  of: string | null = 'e1';
  badge: number | null = 7;
}

describe('initials and colour', () => {
  it('takes the first letter of the first and last word', () => {
    expect(initialsOf('Kasun Perera')).toBe('KP');
    expect(initialsOf('  amara   de   silva ')).toBe('as');
    expect(initialsOf('Nimal')).toBe('N');
    expect(initialsOf('')).toBe('');
  });

  it('keeps a Sinhala letter with its marks, not just its first code unit', () => {
    const initials = initialsOf('කසුන් පෙරේරා');
    expect(initials).toBe('කපෙ');
    expect(Array.from('කසුන්')[0]).toBe('ක');
  });

  it('gives the same name the same colour every time, from three', () => {
    expect(toneOf('Kasun Perera')).toBe(toneOf('Kasun Perera'));
    const tones = new Set(['A B', 'C D', 'E F', 'G H', 'Kasun P', 'Amara S', 'Nimal W', 'Sunil R'].map(toneOf));
    expect([...tones].every((t) => t >= 0 && t <= 2)).toBe(true);
    expect(tones.size).toBeGreaterThan(1);
  });
});

describe('StudentAvatar', () => {
  let http: HttpTestingController;
  const entry: StudentPhotoEntry = { studentId: 's1', version: 5, enrolmentIds: ['e1'] };

  const setup = () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    URL.createObjectURL = vi.fn(() => 'blob:photo');
    URL.revokeObjectURL = vi.fn();
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  };

  afterEach(() => http.verify());

  it('shows initials and the index badge when the student has no photo, and asks for nothing more', async () => {
    const { fixture, el } = setup();
    http.expectOne(`${API}/api/students/photos`).flush([]);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('.initials')?.textContent).toBe('KP');
    expect(el.querySelector('img')).toBeNull();
    expect(el.querySelector('.badge')?.textContent).toBe('7');
    expect(el.querySelector('app-student-avatar')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('finds the photo by an enrolment id, fetches it once by version and shows it', async () => {
    const { fixture, el } = setup();
    http.expectOne(`${API}/api/students/photos`).flush([entry]);
    await fixture.whenStable();
    const request = http.expectOne((r) => r.url === `${API}/api/students/s1/photo`);
    expect(request.request.params.get('v')).toBe('5');
    request.flush(new Blob(['x'], { type: 'image/jpeg' }));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('img')?.getAttribute('src')).toBe('blob:photo');
    expect(el.querySelector('.initials')).toBeNull();
  });

  it('falls back to initials when the picture cannot be shown', async () => {
    const { fixture, el } = setup();
    http.expectOne(`${API}/api/students/photos`).flush([entry]);
    await fixture.whenStable();
    http.expectOne((r) => r.url === `${API}/api/students/s1/photo`).flush(new Blob(['x']));
    await fixture.whenStable();
    fixture.detectChanges();
    el.querySelector('img')!.dispatchEvent(new Event('error'));
    fixture.detectChanges();
    expect(el.querySelector('img')).toBeNull();
    expect(el.querySelector('.initials')?.textContent).toBe('KP');
  });

  it('does not ask for the list at all when it has no id to look up', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.of = null;
    fixture.detectChanges();
    http.expectNone(`${API}/api/students/photos`);
    expect((fixture.nativeElement as HTMLElement).querySelector('.initials')).not.toBeNull();
  });
});
