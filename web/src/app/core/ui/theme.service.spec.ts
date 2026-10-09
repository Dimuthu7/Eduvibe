import { TestBed } from '@angular/core/testing';
import { ThemeService } from './theme.service';

describe('ThemeService', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
    document.head.insertAdjacentHTML('beforeend', '<meta name="theme-color" content="#000">');
  });

  afterEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
    document.querySelector('meta[name="theme-color"]')?.remove();
  });

  it('follows the device until the person chooses', () => {
    const theme = TestBed.inject(ThemeService);
    TestBed.tick();

    expect(theme.preference()).toBe('system');
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
  });

  it('applies and remembers an explicit choice', () => {
    const theme = TestBed.inject(ThemeService);
    theme.set('dark');
    TestBed.tick();

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(localStorage.getItem('eduvibe.theme')).toBe('dark');
    expect(document.querySelector('meta[name="theme-color"]')?.getAttribute('content')).toBe('#0b1b33');
  });

  it('restores the remembered choice on the next visit', () => {
    localStorage.setItem('eduvibe.theme', 'light');

    const theme = TestBed.inject(ThemeService);
    TestBed.tick();

    expect(theme.preference()).toBe('light');
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
  });

  it('ignores a corrupt stored value', () => {
    localStorage.setItem('eduvibe.theme', 'purple');

    expect(TestBed.inject(ThemeService).preference()).toBe('system');
  });
});
