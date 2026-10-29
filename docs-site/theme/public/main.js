export default {
  start: () => {
    // DocFX 2.81's theme toggle is an anchor without href or tabindex.
    const themeToggle = document.querySelector('a[title="Change theme"]');
    if (themeToggle && !themeToggle.hasAttribute('href')) {
      themeToggle.setAttribute('role', 'button');
      themeToggle.setAttribute('aria-label', 'Change theme');
      themeToggle.tabIndex = 0;
      themeToggle.addEventListener('keydown', (event) => {
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault();
          themeToggle.click();
        }
      });
    }
    const content = document.querySelector('main, article');
    if (!content) return;
    if (!content.id) content.id = 'docs-content';
    const skip = document.createElement('a');
    skip.className = 'docs-skip';
    skip.href = `#${content.id}`;
    skip.textContent = 'Skip to content';
    skip.addEventListener('click', () => {
      content.setAttribute('tabindex', '-1');
      content.focus();
    });
    document.body.prepend(skip);
  },
};
