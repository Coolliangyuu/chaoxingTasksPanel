(() => {
  const text = element => (element?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const visible = element => element && element.getClientRects().length > 0;
  const classify = status => {
    if (/未提交|未交|未完成|未作答|未做|未答|待提交|待完成|待作答|待重做|打回|退回|草稿|重做|待订正|待补交/.test(status)) return 'pending';
    if (/已提交|已交|已完成|已作答|已做|已答|待批阅|待批改|待评阅|已批阅|已批改|已评阅|已阅|待评分|已评分/.test(status)) return 'done';
    return 'unknown';
  };
  const clock = Date.now();
  function parseTime(label) {
    const result = { Deadline: null, Estimated: false, Timing: label || null };
    const fullDate = label.match(/(\d{4})[-/.年](\d{1,2})[-/.月](\d{1,2})日?\s+(\d{1,2})[:：](\d{2})(?:[:：](\d{2}))?/g);
    if (fullDate && (fullDate.length > 1 || !/开始|发布/.test(label))) {
      const parts = fullDate.at(-1).match(/\d+/g).map(Number);
      const [year, month, day, hour, min, sec = 0] = parts;
      if (year >= 2000 && year <= 2100 && month >= 1 && month <= 12 && day >= 1 && day <= 31 && hour <= 23 && min <= 59 && sec <= 59) {
        result.Deadline = new Date(Date.UTC(year, month - 1, day, hour - 8, min, sec)).toISOString();
      }
    } else if (/剩余|还剩|后截止/.test(label)) {
      let seconds = 0;
      for (const [, number, unit] of label.matchAll(/(\d+)\s*(天|小时|分钟|分|秒)/g)) {
        seconds += Number(number) * ({ 天: 86400, 小时: 3600, 分钟: 60, 分: 60, 秒: 1 })[unit];
      }
      if (seconds > 0 && seconds < 3153600000) {
        result.Deadline = new Date(clock + seconds * 1000).toISOString();
        result.Estimated = true;
      }
    }
    return result;
  }
  const base = location.protocol === 'https:' ? location.href : 'https://mooc1-api.chaoxing.com/';
  const safeLink = raw => {
    if (!raw) return null;
    try {
      const url = new URL(raw, base);
      const isAssignmentLink = /\/work(?:\/|$)/.test(url.pathname) ||
        (url.hostname === 'mooc1-api.chaoxing.com' &&
         url.pathname === '/mooc-ans/android/mtaskmsgspecial' &&
         url.searchParams.has('taskrefId') && url.searchParams.has('enc_task'));
      if (isAssignmentLink && url.protocol === 'https:' &&
          (url.hostname === 'chaoxing.com' || url.hostname.endsWith('.chaoxing.com'))) return url.href;
    } catch {}
    return null;
  };
  const rows = new Map();
  const selectors = 'ul.nav > li, li[onclick*="goTask"], li[data*="/work/"], tr[data*="/work/"], [data-workid]';
  const candidates = [...document.querySelectorAll(selectors)].filter(visible);
  for (const row of candidates) {
    const option = row.querySelector('[role="option"]') || row;
    const known = row.querySelector('[role="option"]');
    const titleNode = option.querySelector('.work-title, .workTitle, .task-title, .taskName, p, h3, h4');
    const title = text(titleNode) || (titleNode?.getAttribute('title') ?? '').trim();
    if (!title) continue;
    const labels = [...option.querySelectorAll('span')].map(text).filter(Boolean);
    const status = text(option.querySelector('.work-status, .workStatus, .taskStatus, span.status')) ||
      labels.find(label => label.length <= 15 && classify(label) !== 'unknown') ||
      (known ? labels[0] : '') || '状态待确认';
    const course = text(option.querySelector('.course-name, .courseName, .work-course, .course')) ||
      (known ? labels[1] : '') || '—';
    const timing = text(option.querySelector('.fr, .endTime, .deadline, .leftTime, .work-time')) ||
      labels.find(label => /剩余|截止|到期|未开始/.test(label)) || '';
    const raw = row.getAttribute('data') || row.getAttribute('data-href') || option.querySelector('a[href*="/work/"]')?.getAttribute('href');
    const url = safeLink(raw);
    if (!known && !url && classify(status) === 'unknown') continue;
    const key = raw || title + '\n' + course + '\n' + status;
    const category = classify(status);
    rows.set(key, { Title: title, Course: course, Status: status, Category: category, Url: url, ...parseTime(timing) });
  }
  const whole = document.body?.innerText ?? '';
  const partial = [...document.querySelectorAll('a, button')].some(element =>
    visible(element) && /^(下一页|加载更多|查看更多|下页)$/.test(text(element)) &&
    !element.disabled && element.getAttribute('aria-disabled') !== 'true' && !element.closest('.disabled'));
  if (document.querySelector('input[type="password"]') && /登录/.test(whole)) return { Kind: 'login', Rows: [], Partial: false };
  if (/安全验证|访问频繁|人机验证|请完成验证|请进行验证|请求过于频繁/.test(whole.slice(0, 800))) return { Kind: 'verification', Rows: [], Partial: false };
  if (rows.size) return { Kind: 'ready', Rows: [...rows.values()], Partial: partial || rows.size < candidates.length };
  if (/暂无(?:任何|相关)?作业|没有(?:任何|相关)?作业|未发布作业|暂无数据|暂无任务/.test(whole) && candidates.length === 0)
    return { Kind: 'empty', Rows: [], Partial: false };
  return { Kind: 'unknown', Rows: [], Partial: false };
})()
