import { Component, input } from '@angular/core';

@Component({
  selector: 'ta-top-pages',
  templateUrl: './top-pages.html',
})
export class TopPagesComponent {
  pages = input<{ path: string; views: number; avg: string; bounce: string }[]>([]);
}
