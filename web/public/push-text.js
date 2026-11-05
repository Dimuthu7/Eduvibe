/*
 * The words of a phone notification. The server sends only what happened (a kind and a few values); the phone writes it here, in the person's
 * language, using the same translation files as the app. This mirrors src/app/pages/notifications/describe.ts and the helpers in
 * src/app/shared/format.ts: a test (push-text.spec.ts) checks that both give the same words for every kind, so change them together.
 * Plain JavaScript on purpose: the service worker cannot load the app's code.
 */
(function (root) {
  var LOCALES = { en: 'en-GB', si: 'si-LK', ta: 'ta-LK' };
  var MONEY = { en: 'en-LK', si: 'si-LK', ta: 'ta-LK' };

  function knows(locale) {
    try {
      return Intl.DateTimeFormat.supportedLocalesOf([locale]).length > 0;
    } catch (e) {
      return false;
    }
  }

  function nativeLocale(lang) {
    return (lang === 'si' || lang === 'ta') && knows(LOCALES[lang]) ? LOCALES[lang] : null;
  }

  function localeOf(lang) {
    return nativeLocale(lang) || 'en-GB';
  }

  function fill(text, values) {
    return text.replace(/\{(\w+)\}/g, function (whole, name) {
      return name in values ? String(values[name]) : whole;
    });
  }

  function text(value) {
    return typeof value === 'string' ? value : '';
  }

  function number(value) {
    return typeof value === 'number' ? value : Number(value) || 0;
  }

  function lkr(lang, amount) {
    var locale = lang !== 'en' && knows(LOCALES[lang]) ? LOCALES[lang] : 'en-LK';
    return new Intl.NumberFormat(locale, { style: 'currency', currency: 'LKR', maximumFractionDigits: 0 }).format(amount);
  }

  function monthDate(month) {
    return new Date(month + '-01T00:00:00Z');
  }

  function formatMonth(lang, month) {
    return monthDate(month).toLocaleDateString(localeOf(lang), { timeZone: 'UTC', month: 'long', year: 'numeric' });
  }

  function formatMonthShort(lang, month) {
    return monthDate(month).toLocaleDateString(localeOf(lang), { timeZone: 'UTC', month: 'short', year: 'numeric' });
  }

  function formatPeriod(lang, month, lastMonth) {
    return !lastMonth || lastMonth <= month ? formatMonthShort(lang, month) : formatMonthShort(lang, month) + ' – ' + formatMonthShort(lang, lastMonth);
  }

  function formatDayMonth(lang, date) {
    return new Date(date + 'T00:00:00Z').toLocaleDateString(localeOf(lang), { timeZone: 'UTC', day: 'numeric', month: 'short' });
  }

  function formatTime(lang, time) {
    var parts = time.split(':').map(Number);
    var h = parts[0];
    var m = parts[1];
    if (nativeLocale(lang) === 'si-LK') {
      return new Intl.DateTimeFormat('si-LK', { timeZone: 'UTC', hour: 'numeric', minute: '2-digit', hour12: true }).format(new Date(Date.UTC(2000, 0, 1, h, m)));
    }
    return (h % 12 || 12) + ':' + String(m).padStart(2, '0') + ' ' + (h < 12 ? 'AM' : 'PM');
  }

  /** The words for one fee's months: "December 2030" for one month, "Oct 2026 – Dec 2026" for a longer fee. */
  function period(lang, p) {
    var month = text(p.month);
    var last = text(p.lastMonth);
    return last > month ? formatPeriod(lang, month, last) : formatMonth(lang, month);
  }

  function lateText(lang, t, p) {
    var days = number(p.daysLate);
    return fill(t(days === 1 ? 'fees.days_late_one' : 'fees.days_late'), { count: days, date: formatDayMonth(lang, text(p.dueDate)) });
  }

  /** Title and lines for one notification: the same as the app's list shows. */
  function describe(n, t, lang) {
    var p = n.params || {};
    var classTitle = text(p.classTitle);
    switch (n.kind) {
      case 'join_request':
        return {
          title:
            n.count > 1
              ? fill(t('notifications.join_request.many'), { count: n.count, class: classTitle })
              : fill(t('notifications.join_request.one'), { student: text(p.student), class: classTitle }),
          lines: [t('notifications.join_request.hint')],
        };
      case 'results_published':
        return {
          title: fill(t('notifications.results_published.title'), { exam: text(p.exam) }),
          lines: [classTitle, t('notifications.results_published.hint')].filter(Boolean),
        };
      case 'fee_due':
        return {
          title: fill(t('notifications.fee_due.title'), { month: formatMonth(lang, text(p.month)) }),
          lines: [fill(t('notifications.fee_due.body'), { class: classTitle, amount: lkr(lang, number(p.amount)) })],
        };
      case 'fee_before':
        return {
          title: fill(t('notifications.fee_before.title'), { period: period(lang, p) }),
          lines: [fill(t('notifications.fee_before.body'), { class: classTitle, amount: lkr(lang, number(p.amount)), date: formatDayMonth(lang, text(p.dueDate)) })],
        };
      case 'fee_due_today':
        return {
          title: fill(t('notifications.fee_due_today.title'), { amount: lkr(lang, number(p.amount)) }),
          lines: [fill(t('notifications.fee_due_today.body'), { class: classTitle, period: period(lang, p) })],
        };
      case 'fee_overdue':
        return {
          title: fill(t('notifications.fee_overdue.title'), { amount: lkr(lang, number(p.amount)) }),
          lines: [fill(t('notifications.fee_overdue.body'), { class: classTitle, period: period(lang, p) }), lateText(lang, t, p)],
        };
      case 'child_payment':
        return {
          title: fill(t('notifications.child_payment.title'), { student: text(p.student) }),
          lines: [fill(t('notifications.payment_received.body'), { amount: lkr(lang, number(p.amount)), receipt: text(p.receiptNo), class: classTitle })],
        };
      case 'payment_voided':
        return {
          title: t('notifications.payment_voided.title'),
          lines: [fill(t('notifications.payment_received.body'), { amount: lkr(lang, number(p.amount)), receipt: text(p.receiptNo), class: classTitle })],
        };
      case 'child_payment_voided':
        return {
          title: fill(t('notifications.child_payment_voided.title'), { student: text(p.student) }),
          lines: [fill(t('notifications.payment_received.body'), { amount: lkr(lang, number(p.amount)), receipt: text(p.receiptNo), class: classTitle })],
        };
      case 'child_attended': {
        var at = text(p.at);
        var arrival = [fill(t('notifications.child_attended.body'), { class: classTitle, date: formatDayMonth(lang, text(p.date)) })];
        if (at) {
          arrival.push(fill(t('notifications.child_attended.scanned'), { time: formatTime(lang, at) }));
        }

        return {
          title: fill(t(p.late === true ? 'notifications.child_attended.late' : 'notifications.child_attended.title'), { student: text(p.student) }),
          lines: arrival,
        };
      }
      case 'child_absent':
        return {
          title: fill(t('notifications.child_absent.title'), { student: text(p.student) }),
          lines: [fill(t('notifications.child_attended.body'), { class: classTitle, date: formatDayMonth(lang, text(p.date)) })],
        };
      case 'child_results':
        return {
          title: fill(t('notifications.results_published.title'), { exam: text(p.exam) }),
          lines: [fill(t('notifications.about_child'), { student: text(p.student), class: classTitle })],
        };
      case 'result_changed':
        return {
          title: fill(t('notifications.result_changed.title'), { exam: text(p.exam) }),
          lines: [classTitle, t('notifications.results_published.hint')].filter(Boolean),
        };
      case 'child_result_changed':
        return {
          title: fill(t('notifications.result_changed.title'), { exam: text(p.exam) }),
          lines: [fill(t('notifications.about_child'), { student: text(p.student), class: classTitle })],
        };
      case 'payment_received':
        return {
          title: t('notifications.payment_received.title'),
          lines: [fill(t('notifications.payment_received.body'), { amount: lkr(lang, number(p.amount)), receipt: text(p.receiptNo), class: classTitle })],
        };
      case 'extra_class': {
        var note = text(p.note);
        return {
          title: t('notifications.extra_class.title'),
          lines: [fill(t('notifications.extra_class.body'), { class: classTitle, start: formatTime(lang, text(p.start)), end: formatTime(lang, text(p.end)) })].concat(note ? [note] : []),
        };
      }
      case 'fees_unpaid': {
        var classes = Array.isArray(p.classes) ? p.classes : [];
        return {
          title: fill(t('notifications.fees_unpaid.title'), { month: formatMonth(lang, text(p.month)) }),
          lines: classes.map(function (c) {
            return fill(t('notifications.fees_unpaid.line'), { class: text(c.title), count: number(c.count) });
          }),
        };
      }
      default:
        return { title: t('notifications.unknown'), lines: [] };
    }
  }

  /** The title and body of a phone notification, from the message the server sent and the translation file for the person's language. */
  function compose(n, messages, lang) {
    var t = function (key) {
      return messages[key] !== undefined ? messages[key] : key;
    };
    var view = describe(n, t, lang);
    return { title: view.title, body: view.lines.join('\n') };
  }

  root.GuruBookPushText = { compose: compose };
})(typeof self !== 'undefined' ? self : this);
