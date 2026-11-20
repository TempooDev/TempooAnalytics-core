import { Injectable, signal, effect } from '@angular/core';

export type Theme = 'dark' | 'light';

@Injectable({
  providedIn: 'root'
})
export class ThemeService {
  readonly currentTheme = signal<Theme>('dark');

  constructor() {
    const savedTheme = localStorage.getItem('theme-preference') as Theme;
    if (savedTheme === 'light' || savedTheme === 'dark') {
      this.currentTheme.set(savedTheme);
    } else {
      // Default to dark
      this.currentTheme.set('dark');
    }

    effect(() => {
      const theme = this.currentTheme();
      localStorage.setItem('theme-preference', theme);
      
      const html = document.documentElement;
      
      if (theme === 'dark') {
        html.setAttribute('data-theme', 'dark');
        html.classList.add('my-app-dark');
      } else {
        html.removeAttribute('data-theme');
        html.classList.remove('my-app-dark');
      }
    });
  }

  toggleTheme() {
    this.currentTheme.update(t => t === 'dark' ? 'light' : 'dark');
  }

  setTheme(theme: Theme) {
    this.currentTheme.set(theme);
  }
}
