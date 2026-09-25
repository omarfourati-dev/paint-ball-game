// Startseite: Banner unter dem Inhalt und Link „Datenschutzeinstellungen“ – beides nur, wenn der Server Werbung aktiviert hat.
import { Ads } from './ads.js';

const ads = new Ads();
ads.init().then(() => {
  ads.fill(document.getElementById('ad-landing'), 'landing');
  ads.bindPrivacyLink(document.getElementById('privacy-settings'));
});
