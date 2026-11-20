import forgotTemplate from './forgot-password.page.html?raw';

describe('ForgotPasswordPage (template smoke)', () => {
    it('contains email input and submit button', () => {
        const container = document.createElement('div');
        container.innerHTML = forgotTemplate;
        expect(container.querySelector('input[type="email"]')).toBeTruthy();
        expect(container.querySelector('button')).toBeTruthy();
    });
});
