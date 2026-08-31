type User = {
	username: string | null;
	avatarUrl: string | null;
};

const initialState: User | null = null;
export const user = $state(initialState);
