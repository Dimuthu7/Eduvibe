import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { AppConfig } from '../config/app-config';

export interface Venue {
  id: string;
  name: string;
  district: string | null;
  town: string | null;
  address: string | null;
  isActive: boolean;
}

export type VenueInput = Pick<Venue, 'name' | 'district'> & Partial<Pick<Venue, 'town' | 'address' | 'isActive'>>;

export interface Slot {
  id?: string;
  /** 1 (Monday) to 7 (Sunday). */
  day: number;
  /** "HH:mm" or "HH:mm:ss", Asia/Colombo local time. */
  start: string;
  end: string;
}

export type PlaceType = 'institute' | 'venue';

export interface TuitionClass {
  id: string;
  title: string;
  subjectId: string;
  streamId: string;
  examYear: number;
  medium: string;
  placeType: PlaceType;
  placeId: string;
  placeName: string;
  instituteId: string | null;
  monthlyFee: number;
  status: 'Active' | 'Archived';
  archivedAt: string | null;
  slots: Slot[];
}

export interface ClassInput {
  title: string;
  subjectId: string;
  streamId: string;
  examYear: number;
  medium: string;
  instituteId: string | null;
  venueId: string | null;
  monthlyFee: number;
  slots: Slot[];
}

export interface TimetableEntry {
  classId: string;
  title: string;
  subjectId: string;
  day: number;
  start: string;
  end: string;
  placeType: PlaceType;
  placeId: string;
  placeName: string;
  instituteId: string | null;
  overlapsWith: string[];
}

export const MEDIUMS = ['Sinhala', 'Tamil', 'English'] as const;

/** A teacher's venues, classes and weekly timetable. */
@Injectable({ providedIn: 'root' })
export class ClassesService {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(AppConfig).apiBaseUrl}/api/classes`;

  venues() {
    return this.http.get<Venue[]>(`${this.base}/venues`);
  }

  createVenue(venue: VenueInput) {
    return this.http.post<Venue>(`${this.base}/venues`, venue);
  }

  updateVenue(id: string, venue: VenueInput) {
    return this.http.put<Venue>(`${this.base}/venues/${id}`, venue);
  }

  classes(status: 'active' | 'archived' | 'all' = 'active') {
    return this.http.get<TuitionClass[]>(`${this.base}/classes`, { params: { status } });
  }

  classById(id: string) {
    return this.http.get<TuitionClass>(`${this.base}/classes/${id}`);
  }

  createClass(input: ClassInput) {
    return this.http.post<TuitionClass>(`${this.base}/classes`, input);
  }

  updateClass(id: string, input: ClassInput) {
    return this.http.put<TuitionClass>(`${this.base}/classes/${id}`, input);
  }

  archiveClass(id: string) {
    return this.http.post<TuitionClass>(`${this.base}/classes/${id}/archive`, {});
  }

  restoreClass(id: string) {
    return this.http.post<TuitionClass>(`${this.base}/classes/${id}/restore`, {});
  }

  timetable() {
    return this.http.get<TimetableEntry[]>(`${this.base}/timetable`);
  }
}
