(() => {
  if (location.hostname !== 'passport2.chaoxing.com' || location.pathname !== '/mooc/accountManage')
    return { FullName: null, MaskedPhone: null };

  const nameField = document.querySelector('.infoDiv #messageName');
  const name = (nameField?.textContent.trim() || nameField?.getAttribute('value') || '').trim();
  const phone = document.querySelector('.infoDiv #messagePhone')?.textContent.trim() || '';
  const match = phone.match(/(?:^|\D)(1\d{2})[\s-]*(?:\d{4}|[*＊•xX]{3,5})[\s-]*(\d{4})(?!\d)/);
  return {
    FullName: name.length > 0 && name.length <= 48 ? name : null,
    MaskedPhone: match ? match[1] + '****' + match[2] : null
  };
})()
