import { DEFAULT_LOOK, resolveLook } from '@nobodyfalls/render';
import { useEffect, useState, type SyntheticEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client.ts';
import type { PlayerRef } from '../api/types.ts';
import { inviteLink } from '../core/invite.ts';
import { phaseOf, postureOf } from '../core/standing.ts';
import { errorKey, game, useGame } from '../core/store.ts';
import { CharacterCanvas } from '../ui/CharacterCanvas.tsx';
import { Header, Standing } from '../ui/Chrome.tsx';
import ui from '../ui/ui.module.css';
import styles from './screens.module.css';

/** Friends (GAME_DESIGN §4): add, invite, answer, suggestions, the list with today's status. */
export function Friends() {
  const { t, i18n } = useTranslation();
  const friends = useGame((s) => s.friends);
  const requests = useGame((s) => s.requests);
  const suggestions = useGame((s) => s.suggestions);
  const profile = useGame((s) => s.profile);
  const tonight = useGame((s) => s.tonight);
  const [pseudo, setPseudo] = useState('');
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    void game.loadFriends();
    void game.loadProfile();
  }, []);

  /** Runs an action, then reloads the lists; failures become a translated message. */
  const act = async (action: () => Promise<unknown>, success?: string): Promise<void> => {
    setMessage(null);
    try {
      await action();
      if (success) {
        setMessage(success);
      }
      await game.loadFriends();
    } catch (e) {
      setMessage(t(errorKey(e)));
    }
  };

  const ask = (event: SyntheticEvent): void => {
    event.preventDefault();
    const name = pseudo.trim();
    if (!name) {
      return;
    }
    void act(async () => {
      const answer = await api.askFriend(name);
      setPseudo('');
      setMessage(t(answer.outcome === 'sent' ? 'friends.sent' : 'friends.becameFriends', { name }));
    });
  };

  const link = profile ? inviteLink(profile.inviteCode, i18n.language) : null;

  return (
    <div>
      <Header
        night={tonight?.nightNumber ?? null}
        midnightAt={tonight?.seasonOpen ? tonight.midnightAt : null}
      />
      <main className={ui.page}>
        <h1 className={ui.heading}>{t('friends.title')}</h1>

        <form className={ui.section} onSubmit={ask}>
          <label htmlFor="friend-pseudo" className={ui.subheading}>
            {t('friends.addLabel')}
          </label>
          <div className={ui.field}>
            <input
              id="friend-pseudo"
              className={ui.input}
              value={pseudo}
              placeholder={t('friends.addPlaceholder')}
              autoComplete="off"
              autoCapitalize="none"
              onChange={(e) => {
                setPseudo(e.target.value);
              }}
            />
            <button type="submit" className={ui.primary}>
              {t('friends.add')}
            </button>
          </div>
          {message && (
            <p className={ui.notice} role="status">
              {message}
            </p>
          )}
        </form>

        {link && <InviteLink link={link} />}

        {requests.incoming.length > 0 && (
          <section className={ui.section}>
            <h2 className={ui.subheading}>{t('friends.incoming')}</h2>
            <ul className={ui.list}>
              {requests.incoming.map((r) => (
                <PersonRow key={r.id} player={r.player}>
                  <button
                    type="button"
                    className={ui.secondary}
                    onClick={() => void act(() => api.acceptRequest(r.id))}
                  >
                    {t('friends.accept')}
                  </button>
                  <button
                    type="button"
                    className={ui.secondary}
                    onClick={() => void act(() => api.declineRequest(r.id))}
                  >
                    {t('friends.decline')}
                  </button>
                </PersonRow>
              ))}
            </ul>
          </section>
        )}

        {suggestions.length > 0 && (
          <section className={ui.section}>
            <h2 className={ui.subheading}>{t('friends.suggestions')}</h2>
            <ul className={ui.list}>
              {suggestions.map((s) => (
                <PersonRow
                  key={s.player.id}
                  player={s.player}
                  detail={t('friends.nights', { count: s.nights })}
                >
                  <button
                    type="button"
                    className={ui.secondary}
                    onClick={() => void act(() => api.askFriend(s.player.pseudo))}
                  >
                    {t('friends.add')}
                  </button>
                </PersonRow>
              ))}
            </ul>
          </section>
        )}

        <section className={ui.section}>
          <h2 className={ui.subheading}>{t('friends.list', { count: friends.length })}</h2>
          {friends.length === 0 && <p className={ui.text}>{t('friends.empty')}</p>}
          <ul className={ui.list}>
            {friends.map((f) => {
              const needsHand =
                f.status === 'alive' &&
                f.today !== null &&
                !f.today.immune &&
                f.today.received < f.today.required;
              return (
                <PersonRow
                  key={f.player.id}
                  player={f.player}
                  standing={f.today}
                  fallen={f.status === 'fallen'}
                  actions={
                    <>
                      <button
                        type="button"
                        className={ui.secondary}
                        onClick={() => void act(() => api.removeFriend(f.player.id))}
                      >
                        {t('friends.remove')}
                      </button>
                      <button
                        type="button"
                        className={ui.secondary}
                        onClick={() => {
                          if (window.confirm(t('friends.confirmBlock', { name: f.player.pseudo }))) {
                            void act(() => api.block(f.player.id));
                          }
                        }}
                      >
                        {t('friends.block')}
                      </button>
                    </>
                  }
                >
                  {needsHand && (tonight?.handsLeft ?? 0) > 0 && (
                    <button
                      type="button"
                      className={ui.primary}
                      style={{ height: 44 }}
                      onClick={() => void act(() => game.hold(f.player.id))}
                    >
                      {t('friends.hold')}
                    </button>
                  )}
                </PersonRow>
              );
            })}
          </ul>
        </section>

        {requests.outgoing.length > 0 && (
          <section className={ui.section}>
            <h2 className={ui.subheading}>{t('friends.outgoing')}</h2>
            <ul className={ui.list}>
              {requests.outgoing.map((r) => (
                <PersonRow key={r.id} player={r.player}>
                  <button
                    type="button"
                    className={ui.secondary}
                    onClick={() => void act(() => api.cancelRequest(r.id))}
                  >
                    {t('common.cancel')}
                  </button>
                </PersonRow>
              ))}
            </ul>
          </section>
        )}
      </main>
    </div>
  );
}

function InviteLink({ link }: { link: string }) {
  const { t } = useTranslation();
  const [copied, setCopied] = useState(false);
  const canShare = typeof navigator.share === 'function';
  return (
    <section className={ui.section}>
      <h2 className={ui.subheading}>{t('friends.invite')}</h2>
      <p className={ui.text}>{t('friends.inviteHint')}</p>
      <div className={ui.field}>
        <input
          className={ui.input}
          readOnly
          value={link}
          aria-label={t('friends.invite')}
          onFocus={(e) => {
            e.target.select();
          }}
        />
        <button
          type="button"
          className={ui.secondary}
          onClick={() => {
            if (canShare) {
              void navigator.share({ text: t('friends.shareText'), url: link }).catch(() => undefined);
            } else {
              void navigator.clipboard.writeText(link).then(() => {
                setCopied(true);
              });
            }
          }}
        >
          {canShare ? t('friends.share') : t('friends.copy')}
        </button>
      </div>
      {copied && (
        <p className={ui.notice} role="status">
          {t('friends.copied')}
        </p>
      )}
    </section>
  );
}

interface RowProps {
  readonly player: PlayerRef;
  readonly standing?: { received: number; required: number; immune: boolean } | null;
  readonly fallen?: boolean;
  readonly detail?: string;
  readonly actions?: React.ReactNode;
  readonly children?: React.ReactNode;
}

function PersonRow({ player, standing = null, fallen = false, detail, actions, children }: RowProps) {
  const { t } = useTranslation();
  const renderKeys = useGame((s) => s.renderKeys);
  return (
    <li className={ui.row}>
      <CharacterCanvas
        look={player.appearance ? resolveLook(player.appearance, renderKeys) : DEFAULT_LOOK}
        posture={postureOf(standing, fallen ? 'fallen' : 'alive')}
        phase={phaseOf(player.id)}
        size={64}
      />
      <div className={ui.rowBody}>
        <span className={ui.name}>{player.pseudo}</span>
        {fallen ? (
          <span className={ui.status}>{t('common.fallen')}</span>
        ) : standing ? (
          <Standing standing={standing} />
        ) : null}
        {detail && <span className={ui.status}>{detail}</span>}
        {actions && <span className={styles.actions}>{actions}</span>}
      </div>
      {children}
    </li>
  );
}
