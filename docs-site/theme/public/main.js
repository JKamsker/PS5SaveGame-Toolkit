export default {
  start: () => {
    // DocFX inserts its navbar asynchronously after start. Upgrade the theme
    // anchor once it arrives; it otherwise has neither href nor keyboard focus.
    const prepareThemeToggle = () => {
      const toggle = document.querySelector('a[title="Change theme"]');
      if (!toggle) return false;
      toggle.setAttribute('role', 'button');
      toggle.setAttribute('aria-label', 'Change theme');
      toggle.tabIndex = 0;
      toggle.addEventListener('keydown', (event) => {
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault();
          toggle.click();
        }
      });
      return true;
    };
    if (!prepareThemeToggle()) {
      const observer = new MutationObserver(() => {
        if (prepareThemeToggle()) observer.disconnect();
      });
      observer.observe(document.querySelector('header') ?? document.body, { childList: true, subtree: true });
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
