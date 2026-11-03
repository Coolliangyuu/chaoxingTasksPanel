(credential => {
  if (location.origin !== 'https://passport2.chaoxing.com' || location.pathname !== '/login') return false;

  const user = document.querySelector('input#phone[type="text"], input#phone[type="tel"]');
  const password = document.querySelector('input#pwd[type="password"]');
  const button = document.querySelector('button#loginBtn');
  if (!user || !password || !button || button.disabled || !user.form ||
      password.form !== user.form || button.form !== user.form ||
      !/\bloginByPhoneAndPwd\s*\(/.test(button.getAttribute('onclick') ?? '') ||
      typeof window.loginByPhoneAndPwd !== 'function') return false;

  if (user.value || password.value) return false;
  if ((user.maxLength > 0 && credential.UserName.length > user.maxLength) ||
      (password.maxLength > 0 && credential.Password.length > password.maxLength)) return false;

  const keepSession = document.querySelector('.auto-lg-next .check-input');
  const retention = document.querySelector('input#retainlogin[type="hidden"]');
  if (!keepSession || !retention) return false;
  if (!keepSession.classList.contains('checked-input')) keepSession.click();
  if (!keepSession.classList.contains('checked-input') || retention.value !== '1') return false;

  user.value = credential.UserName;
  password.value = credential.Password;
  for (const field of [user, password]) {
    field.dispatchEvent(new Event('input', { bubbles: true }));
    field.dispatchEvent(new Event('change', { bubbles: true }));
  }
  button.click();
  return true;
})
