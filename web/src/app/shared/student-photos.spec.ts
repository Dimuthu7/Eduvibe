import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { StudentPhotoEntry } from '../core/api/students.service';
import { User } from '../core/auth/auth.models';
import { AuthService } from '../core/auth/auth.service';
import { AppConfig } from '../core/config/app-config';
import { StudentPhotos } from './student-photos';

const API = 'http://api.test';
const entry = (version = 1): StudentPhotoEntry => ({ studentId: 's1', version, enrolmentIds: ['e1', 'e2'] });
const user = (id: string) => ({ id, roles: ['Teacher'] }) as User;

describe('StudentPhotos', () => {
  let http: HttpTestingController;
  let photos: StudentPhotos;
  let counter: number;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(AppConfig), 'apiBaseUrl', 'get').mockReturnValue(API);
    counter = 0;
    URL.createObjectURL = vi.fn(() => `blob:${++counter}`);
    URL.revokeObjectURL = vi.fn();
    TestBed.inject(AuthService).user.set(user('u1'));
    photos = TestBed.inject(StudentPhotos);
    TestBed.tick();
  });

  afterEach(() => http.verify());

  const loadIndex = (rows: StudentPhotoEntry[]) => {
    photos.ensureIndex();
    http.expectOne(`${API}/api/students/photos`).flush(rows);
  };

  it('looks up a student by their own id or by any of the teacher\'s enrolment ids', () => {
    loadIndex([entry()]);
    expect(photos.entryOf('s1')?.version).toBe(1);
    expect(photos.entryOf('e1')?.studentId).toBe('s1');
    expect(photos.entryOf('e2')?.studentId).toBe('s1');
    expect(photos.entryOf('other')).toBeUndefined();
    expect(photos.entryOf(null)).toBeUndefined();
  });

  it('asks for the list once however many avatars need it', () => {
    photos.ensureIndex();
    photos.ensureIndex();
    http.expectOne(`${API}/api/students/photos`).flush([]);
    photos.ensureIndex();
    http.expectNone(`${API}/api/students/photos`);
  });

  it('fetches each picture once and keeps it', () => {
    loadIndex([entry(3)]);
    photos.want(photos.entryOf('e1'));
    photos.want(photos.entryOf('s1'));
    http.expectOne((r) => r.url === `${API}/api/students/s1/photo`).flush(new Blob(['x']));
    expect(photos.urlOf('e1')).toBe('blob:1');
    photos.want(photos.entryOf('s1'));
    http.expectNone((r) => r.url === `${API}/api/students/s1/photo`);
  });

  it('never has more than a handful of pictures coming at once', () => {
    const many = Array.from({ length: 10 }, (_, i) => ({ studentId: `s${i}`, version: 1, enrolmentIds: [] }));
    loadIndex(many);
    many.forEach((m) => photos.want(photos.entryOf(m.studentId)));
    const open = http.match((r) => r.url.endsWith('/photo'));
    expect(open.length).toBe(6);
    open[0].flush(new Blob(['x']));
    expect(http.match((r) => r.url.endsWith('/photo')).length).toBe(1);
    open.slice(1).forEach((r) => r.flush(new Blob(['x'])));
    http.match((r) => r.url.endsWith('/photo')).forEach((r) => r.flush(new Blob(['x'])));
  });

  it('shows an uploaded picture at once and lets go of the one it replaces', async () => {
    loadIndex([entry(1)]);
    photos.want(photos.entryOf('s1'));
    http.expectOne((r) => r.url.endsWith('/s1/photo') && r.method === 'GET').flush(new Blob(['old']));
    expect(photos.urlOf('s1')).toBe('blob:1');

    const saved = photos.upload('s1', new Blob(['new'], { type: 'image/jpeg' }));
    const put = http.expectOne((r) => r.method === 'PUT');
    expect(put.request.headers.get('Content-Type')).toBe('image/jpeg');
    put.flush({ studentId: 's1', version: 2, enrolmentIds: ['e1'] });
    await saved;
    expect(photos.entryOf('s1')?.version).toBe(2);
    expect(photos.urlOf('e1')).toBe('blob:2');
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:1');
  });

  it('forgets a removed photo', async () => {
    loadIndex([entry()]);
    const done = photos.remove('s1');
    http.expectOne((r) => r.method === 'DELETE').flush(null);
    await done;
    expect(photos.entryOf('s1')).toBeUndefined();
  });

  it('keeps initials showing when the list cannot be loaded', () => {
    photos.ensureIndex();
    http.expectOne(`${API}/api/students/photos`).flush('x', { status: 500, statusText: 'Server Error' });
    expect(photos.entryOf('s1')).toBeUndefined();
  });

  it('drops everything when a different person signs in', () => {
    loadIndex([entry()]);
    photos.want(photos.entryOf('s1'));
    http.expectOne((r) => r.url.endsWith('/s1/photo')).flush(new Blob(['x']));
    expect(photos.urlOf('s1')).toBe('blob:1');

    TestBed.inject(AuthService).user.set(user('u2'));
    TestBed.tick();
    expect(photos.entryOf('s1')).toBeUndefined();
    expect(photos.urlOf('s1')).toBeNull();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:1');
    photos.ensureIndex();
    http.expectOne(`${API}/api/students/photos`).flush([]);
  });

  it('loads a student\'s own photo, and treats an empty answer as no photo', () => {
    photos.loadMine();
    http.expectOne(`${API}/api/students/me/photo`).flush(new Blob(['x']));
    expect(photos.mine()).toBe('blob:1');
    photos.loadMine();
    http.expectOne(`${API}/api/students/me/photo`).flush(new Blob([]));
    expect(photos.mine()).toBeNull();
  });
});
