(() => {
  if (location.hostname !== 'passport2.chaoxing.com' || document.getElementById('study-tasks-login-style')) return;
  const style = document.createElement('style');
  style.id = 'study-tasks-login-style';
  style.textContent = `
    body { background: #f1f6fa !important; }
    .lg-container { min-height: 100%; height: auto !important; background: transparent !important; }
    .lg-container .main {
      position: relative !important;
      top: auto !important;
      left: auto !important;
      transform: none !important;
      margin: 14px auto 26px !important;
      max-width: calc(100% - 24px);
    }
    .lg-container .default-login-main { background: transparent !important; }
    .lg-container .default-login {
      border-radius: 16px !important;
      border: 1px solid #dbe7ef !important;
      box-shadow: 0 12px 32px #173c5416 !important;
    }
    #phone, #pwd, #vercode { font-family: "Microsoft YaHei UI", sans-serif !important; }
    #showlogintext { color: #152538 !important; }
    @media (max-width: 940px) {
      .lg-container .default-login { width: 100% !important; }
      .default-login .left { width: 58%; padding-left: 5%; padding-right: 5%; }
      .default-login .right { width: 42%; padding-left: 5%; padding-right: 5%; }
      .default-login .lg-item input, .default-login .btn-big-blue { max-width: 100%; }
    }
  `;
  document.head.appendChild(style);
})()
