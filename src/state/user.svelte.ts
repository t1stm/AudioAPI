type User = {
	username: string | null;
	avatarUrl: string | null;
};

const initialState: User | null = null;
export let user = $state(initialState);
